using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using ClaudeUsingRevit.Setup;

// SetupTest <payload.zip> <setup.exe>   (APPDATA, USERPROFILE, ProgramFiles point at a temp folder: setup-test.sh)
static class Program
{
    static int _fail;
    static void Check(string what, bool ok, string detail = null)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}{(ok || detail == null ? "" : ": " + detail)}");
        if (!ok) _fail++;
    }

    static int Main(string[] a)
    {
        var core = new InstallerCore { Log = s => Console.WriteLine("   | " + s) };
        string appData = Environment.GetEnvironmentVariable("APPDATA"), home = Environment.GetEnvironmentVariable("USERPROFILE");
        int payloadCommands;
        using (var zip = ZipFile.OpenRead(a[0]))
        {
            payloadCommands = zip.Entries.Count(e => e.FullName.StartsWith("commands/") && e.FullName.EndsWith(".cs"));
            var payloadRevit = InstallerCore.PayloadRevit(zip);
            Check("payload has Revit 2023-2027", string.Join(",", payloadRevit) == "2023,2024,2025,2026,2027", string.Join(",", payloadRevit));
            var found = core.InstalledRevit();
            Check("finds installed Revit 2023 + 2025", string.Join(",", found) == "2023,2025", string.Join(",", found));
            core.Install(zip, found, skills: true, connectClaude: true);
            core.Install(zip, found, skills: true, connectClaude: true);     // again: same result
        }
        foreach (string v in new[] { "2023", "2025" })
        {
            string bin = Path.Combine(appData, "claude-using-revit", "bin", v);
            Check($"{v}: add-in dll", File.Exists(Path.Combine(bin, "ClaudeUsingRevit.dll")));
            Check($"{v}: IronPython + Wpf", File.Exists(Path.Combine(bin, "IronPython.dll")) && File.Exists(Path.Combine(bin, "IronPython.Wpf.dll")));
            Check($"{v}: Python Lib", File.Exists(Path.Combine(bin, "Lib", "BaseHTTPServer.py")) && File.Exists(Path.Combine(bin, "Lib", "json", "__init__.py")));
            Check($"{v}: roslyn", File.Exists(Path.Combine(bin, "roslyn", "ClaudeUsingRevit.Compiler.dll")) && File.Exists(Path.Combine(bin, "roslyn", "Microsoft.CodeAnalysis.CSharp.dll")));
            string man = Path.Combine(appData, "Autodesk", "Revit", "Addins", v, "ClaudeUsingRevit.addin");
            Check($"{v}: manifest points at the dll", File.Exists(man) && File.ReadAllText(man).Contains(Path.Combine(bin, "ClaudeUsingRevit.dll")));
        }
        Check("2024 not installed (not on this PC)", !Directory.Exists(Path.Combine(appData, "claude-using-revit", "bin", "2024")));
        string data = Path.Combine(appData, "claude-using-revit");
        Check("ribbon tab", File.Exists(Path.Combine(data, "extension", "claude using revit.tab", "PrintSet.panel", "Export PDF.pushbutton", "script.py")));
        Check("host package cur", File.Exists(Path.Combine(data, "extension", "lib", "cur", "forms.py")));
        Check("stale extension file removed", !File.Exists(Path.Combine(data, "extension", "stale.py")));
        int installedCommands = Directory.GetFiles(Path.Combine(data, "commands"), "*.cs").Length;      // every command of the payload, no stale one
        Check("commands", payloadCommands > 0 && installedCommands == payloadCommands && !File.Exists(Path.Combine(data, "commands", "old_cmd.cs")),
              installedCommands + " installed, " + payloadCommands + " in the payload");
        Check("config.json kept (custom port)", File.ReadAllText(Path.Combine(data, "config.json")).Contains("8975"));
        Check("old pyRevit extension removed", !Directory.Exists(Path.Combine(appData, "pyRevit", "Extensions", "ClaudeUsingRevit.extension")));
        Check("skills", File.Exists(Path.Combine(home, ".claude", "skills", "drafting-session", "SKILL.md")));
        Check("user's own skill kept", File.Exists(Path.Combine(home, ".claude", "skills", "my-own", "SKILL.md")));
        Check("agents + domain", File.Exists(Path.Combine(home, ".claude", "agents", "drafting-visual-checker.md"))
                                 && File.Exists(Path.Combine(home, ".claude", "drafting-domain", "drafting-tools.md"))
                                 && File.Exists(Path.Combine(home, ".claude", "drafting-domain", "workflows", "floor-plan.md")));

        string cj = Path.Combine(home, ".claude.json");
        var root = (JsonObject)MiniJson.Parse(File.ReadAllText(cj));
        var servers = (JsonObject)root.Get("mcpServers");
        var entry = servers.Get("claude-using-revit") as JsonObject;
        Check("Claude: MCP entry (http, custom port)", entry != null && (string)entry.Get("type") == "http" && (string)entry.Get("url") == "http://127.0.0.1:8975/mcp");
        Check("Claude: other servers kept", servers.Get("revit-mcp") != null && servers.Get("other") != null);
        Check("Claude: other settings kept exactly", ((JsonNumber)root.Get("numStartups")).Text == "123" && ((JsonNumber)root.Get("big")).Text == "12345678901234567890"
              && (string)root.Get("name") == "Huy \"x\" Việt\n" && root.Get("tipsHistory") is JsonObject && (bool)root.Get("flag") == true);
        Check("Claude: key order kept", root[0].Key == "numStartups" && root[root.Count - 1].Key == "mcpServers");
        Check("Claude: backup made", Directory.GetFiles(home, ".claude.json.bak-*").Length >= 1);

        core.Uninstall();
        Check("uninstall: manifests gone", core.InstalledAddins().Count == 0);
        Check("uninstall: bin gone", !Directory.Exists(Path.Combine(data, "bin")));
        var after = (JsonObject)((JsonObject)MiniJson.Parse(File.ReadAllText(cj))).Get("mcpServers");
        Check("uninstall: MCP entry gone, others kept", after.Get("claude-using-revit") == null && after.Get("revit-mcp") != null);

        // the exe carries the payload
        var exe = Assembly.LoadFile(Path.GetFullPath(a[1]));
        using (var s = exe.GetManifestResourceStream("payload.zip"))
            Check("Setup.exe carries payload.zip", s != null && s.Length == new FileInfo(a[0]).Length, s?.Length.ToString());
        Console.WriteLine(_fail == 0 ? "ALL PASSED" : _fail + " FAILED");
        return _fail == 0 ? 0 : 1;
    }
}
