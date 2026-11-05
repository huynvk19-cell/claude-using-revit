using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsingRevit.Setup
{
    /// <summary>
    /// What Setup.exe does, without its window (the test runs this on any OS).
    /// The payload (a zip inside the exe, made by make-setup.sh) holds:
    ///   addin/&lt;revit&gt;/   the add-in for that Revit (ClaudeUsingRevit.dll, IronPython, roslyn/)
    ///   python/Lib/        the Python standard library (one copy, put next to each add-in)
    ///   extension/         the ribbon tools          commands/   revit-commands/*.cs
    ///   claude/            skills/, agents/, drafting-domain/ for ~/.claude      version.txt
    /// It installs to %APPDATA%\claude-using-revit (the user's own folders: no admin rights), writes one
    /// %APPDATA%\Autodesk\Revit\Addins\&lt;revit&gt;\ClaudeUsingRevit.addin per Revit, adds the MCP server to
    /// Claude Code (~/.claude.json, a backup first) and an entry in Windows' "Apps" list for uninstalling.
    /// </summary>
    public class InstallerCore
    {
        public const string McpName = "claude-using-revit";
        public const string AddinFile = "ClaudeUsingRevit.addin";
        const string AddInId = "8D3C5F2E-6B1A-4C9E-9A57-2F0D6C1E4B73";

        public Action<string> Log = s => { };

        public readonly string AppData = Env("APPDATA");
        public readonly string Home = Env("USERPROFILE");
        public readonly string ProgramFiles = Env("ProgramFiles");
        public string Data => Path.Combine(AppData, "claude-using-revit");
        public string ClaudeDir => Path.Combine(Home, ".claude");
        public string ClaudeJson => Path.Combine(Home, ".claude.json");
        public int Port { get; private set; } = 8970;

        static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v)) throw new InvalidOperationException("environment variable " + name + " is not set");
            return v;
        }

        // ---- what is here ---------------------------------------------------------------------

        /// <summary>Revit versions installed on this PC (Program Files\Autodesk\Revit 20xx\Revit.exe).</summary>
        public List<string> InstalledRevit()
        {
            var list = new List<string>();
            string root = Path.Combine(ProgramFiles, "Autodesk");
            if (!Directory.Exists(root)) return list;
            foreach (string d in Directory.GetDirectories(root, "Revit 20*"))
            {
                var m = Regex.Match(Path.GetFileName(d), @"^Revit (20\d\d)$");
                if (m.Success && File.Exists(Path.Combine(d, "Revit.exe"))) list.Add(m.Groups[1].Value);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static List<string> PayloadRevit(ZipArchive zip)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var e in zip.Entries)
            {
                var m = Regex.Match(e.FullName, @"^addin/(20\d\d)/ClaudeUsingRevit\.dll$");
                if (m.Success) set.Add(m.Groups[1].Value);
            }
            return set.ToList();
        }

        public static string PayloadVersion(ZipArchive zip)
        {
            var e = zip.GetEntry("version.txt");
            if (e == null) return "";
            using (var r = new StreamReader(e.Open())) return r.ReadToEnd().Trim();
        }

        public static bool RevitRunning() => Process.GetProcessesByName("Revit").Length > 0;
        public static bool ClaudeRunning() => Process.GetProcessesByName("Claude").Length > 0
                                              || Process.GetProcessesByName("claude").Length > 0;

        public List<string> InstalledAddins()
        {
            var list = new List<string>();
            string root = Path.Combine(AppData, "Autodesk", "Revit", "Addins");
            if (!Directory.Exists(root)) return list;
            foreach (string d in Directory.GetDirectories(root))
                if (File.Exists(Path.Combine(d, AddinFile))) list.Add(Path.GetFileName(d));
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ---- install --------------------------------------------------------------------------

        public void Install(ZipArchive zip, IEnumerable<string> revitVersions, bool skills, bool connectClaude)
        {
            var versions = revitVersions.ToList();
            Directory.CreateDirectory(Data);

            foreach (string v in versions)
            {
                string bin = Path.Combine(Data, "bin", v);
                Log($"Revit {v}: chép add-in vào {bin}");
                ReplaceDir(bin);
                int n = Extract(zip, "addin/" + v + "/", bin);
                n += Extract(zip, "python/Lib/", Path.Combine(bin, "Lib"));
                if (n == 0) throw new InvalidOperationException("bộ cài không có add-in cho Revit " + v);
                string addins = Path.Combine(AppData, "Autodesk", "Revit", "Addins", v);
                Directory.CreateDirectory(addins);
                File.WriteAllText(Path.Combine(addins, AddinFile), Manifest(Path.Combine(bin, "ClaudeUsingRevit.dll")), new UTF8Encoding(true));
                Log($"Revit {v}: đã đăng ký add-in ({Path.Combine(addins, AddinFile)})");
            }

            Log("Chép các nút của tab claude using revit");
            ReplaceDir(Path.Combine(Data, "extension"));
            Extract(zip, "extension/", Path.Combine(Data, "extension"));

            string commands = Path.Combine(Data, "commands");
            Directory.CreateDirectory(commands);
            foreach (string f in Directory.GetFiles(commands, "*.cs")) File.Delete(f);
            int nc = Extract(zip, "commands/", commands);
            Log($"Chép {nc} lệnh cho Claude");

            WriteConfigIfMissing();
            RemoveOldPyRevitExtension();

            if (skills)
            {
                Log("Chép bộ kỹ năng (skills, agents, drafting-domain) cho Claude vào " + ClaudeDir);
                foreach (string skill in TopFolders(zip, "claude/skills/"))
                {
                    string dst = Path.Combine(ClaudeDir, "skills", skill);
                    ReplaceDir(dst);
                    Extract(zip, "claude/skills/" + skill + "/", dst);
                }
                Extract(zip, "claude/agents/", Path.Combine(ClaudeDir, "agents"));
                Extract(zip, "claude/drafting-domain/", Path.Combine(ClaudeDir, "drafting-domain"));
            }
            if (connectClaude) ConnectClaude();
        }

        static string Manifest(string dll) =>
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>claude using revit</Name>
    <Assembly>{dll}</Assembly>
    <AddInId>{AddInId}</AddInId>
    <FullClassName>ClaudeUsingRevit.App</FullClassName>
    <VendorId>CLAUDEUSINGREVIT</VendorId>
    <VendorDescription>claude using revit: ribbon tools + MCP server for Claude</VendorDescription>
  </AddIn>
</RevitAddIns>
";

        void WriteConfigIfMissing()
        {
            string cfg = Path.Combine(Data, "config.json");
            if (File.Exists(cfg))
            {
                try
                {
                    if (MiniJson.Parse(File.ReadAllText(cfg)) is JsonObject o && o.Get("port") is JsonNumber p
                        && int.TryParse(p.Text, out int port) && port > 0) Port = port;
                }
                catch { /* keep the default port */ }
                return;
            }
            File.WriteAllText(cfg, "{\n  \"port\": 8970,\n  \"autoStart\": true\n}\n");
        }

        void RemoveOldPyRevitExtension()
        {
            string old = Path.Combine(AppData, "pyRevit", "Extensions", "ClaudeUsingRevit.extension");
            if (!Directory.Exists(old)) return;
            try { Directory.Delete(old, true); Log("Đã gỡ bản pyRevit cũ (ClaudeUsingRevit.extension) để không bị hai tab"); }
            catch (Exception ex) { Log("Chưa gỡ được bản pyRevit cũ: " + ex.Message); }
        }

        /// <summary>Add (or update) the MCP server in Claude Code's user settings (~/.claude.json).</summary>
        public void ConnectClaude()
        {
            string url = $"http://127.0.0.1:{Port}/mcp";
            JsonObject root;
            if (File.Exists(ClaudeJson))
            {
                string text = File.ReadAllText(ClaudeJson);
                try { root = MiniJson.Parse(text) as JsonObject ?? throw new FormatException("not an object"); }
                catch (Exception ex)
                {
                    Log($"Không đọc được {ClaudeJson} ({ex.Message}). Tự nối bằng lệnh: claude mcp add --transport http --scope user {McpName} {url}");
                    return;
                }
                string backup = ClaudeJson + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(ClaudeJson, backup, true);
            }
            else root = new JsonObject();
            var servers = root.Get("mcpServers") as JsonObject;
            if (servers == null) { servers = new JsonObject(); root.Set("mcpServers", servers); }
            var entry = new JsonObject();
            entry.Set("type", "http");
            entry.Set("url", url);
            servers.Set(McpName, entry);
            WriteAtomic(ClaudeJson, MiniJson.Write(root) + "\n");
            Log($"Đã nối Claude Code với Revit: máy chủ MCP \"{McpName}\" = {url}");
            if (servers.Get("revit-mcp") != null)
                Log("Lưu ý: Claude còn cấu hình Revit MCP cũ (\"revit-mcp\"). Không cần nữa thì gỡ được.");
        }

        public void DisconnectClaude()
        {
            if (!File.Exists(ClaudeJson)) return;
            try
            {
                if (!(MiniJson.Parse(File.ReadAllText(ClaudeJson)) is JsonObject root)) return;
                if (root.Get("mcpServers") is JsonObject servers && servers.Remove(McpName))
                {
                    File.Copy(ClaudeJson, ClaudeJson + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                    WriteAtomic(ClaudeJson, MiniJson.Write(root) + "\n");
                    Log("Đã bỏ máy chủ MCP \"" + McpName + "\" khỏi Claude Code");
                }
            }
            catch (Exception ex) { Log("Không sửa được " + ClaudeJson + ": " + ex.Message); }
        }

        // ---- uninstall ------------------------------------------------------------------------

        /// <summary>Remove the add-in from every Revit; keep the settings, the tools' folders and Claude's skills.</summary>
        public void Uninstall()
        {
            foreach (string v in InstalledAddins())
            {
                File.Delete(Path.Combine(AppData, "Autodesk", "Revit", "Addins", v, AddinFile));
                Log($"Revit {v}: đã gỡ add-in");
            }
            string bin = Path.Combine(Data, "bin");
            if (Directory.Exists(bin)) { Directory.Delete(bin, true); Log("Đã xoá " + bin); }
            DisconnectClaude();
            Log("Giữ lại cài đặt và dữ liệu ở " + Data + " (xoá tay nếu không dùng nữa)");
        }

        // ---- files ----------------------------------------------------------------------------

        static IEnumerable<string> TopFolders(ZipArchive zip, string prefix)
            => zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.FullName.Length > prefix.Length)
                          .Select(e => e.FullName.Substring(prefix.Length).Split('/')[0])
                          .Where(n => n.Length > 0 && zip.Entries.Any(x => x.FullName.StartsWith(prefix + n + "/", StringComparison.Ordinal)))
                          .Distinct();

        /// <summary>Every file under prefix, to target (folders made, files replaced). Returns the file count.</summary>
        public static int Extract(ZipArchive zip, string prefix, string target)
        {
            int n = 0;
            string full = Path.GetFullPath(target);
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith(prefix, StringComparison.Ordinal) || e.FullName.EndsWith("/")) continue;
                string rel = e.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
                string dst = Path.GetFullPath(Path.Combine(full, rel));
                if (!dst.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;    // no ../ escapes
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                e.ExtractToFile(dst, true);
                n++;
            }
            return n;
        }

        static void ReplaceDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); }
                catch (UnauthorizedAccessException) { throw new IOException("Không thay được " + dir + ": hãy đóng Revit rồi cài lại."); }
                catch (IOException) { throw new IOException("Không thay được " + dir + ": hãy đóng Revit rồi cài lại."); }
            }
            Directory.CreateDirectory(dir);
        }

        static void WriteAtomic(string file, string text)
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }
    }
}
