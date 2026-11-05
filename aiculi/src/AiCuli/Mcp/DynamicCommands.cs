using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiCuli.Core;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace AiCuli.Mcp
{
    /// <summary>
    /// The MCP commands of the repo (revit-commands/*.cs, installed to %APPDATA%\AiCuli\commands): one file
    /// = one tool. The file starts with a header comment
    ///     /* mcp-tool { "description", "inputSchema", "timeoutSeconds", "readOnly", "references" } */
    /// and has one public static method  object Run(UIApplication app, JObject args)  (it opens its own
    /// transactions). A file is compiled on its first call and again whenever it changes, so an edit applies
    /// on the next call, with no restart of Revit or of the Claude session.
    /// </summary>
    public static class DynamicCommands
    {
        static readonly Regex Header = new Regex(@"/\*\s*mcp-tool\s*([\s\S]*?)\*/", RegexOptions.Compiled);
        public static readonly Regex NameRule = new Regex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled);

        public class Info
        {
            public string Name;
            public string File;
            public DateTime Stamp;
            public string Description = "";
            public JObject InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() };
            public int TimeoutSeconds = 300;
            public bool ReadOnly;
            public List<string> References = new List<string>();
            public string HeaderError;
        }

        public static Info ReadHeader(string name, string file, string source)
        {
            var info = new Info { Name = name, File = file };
            var m = Header.Match(source);
            if (!m.Success) { info.HeaderError = "no /* mcp-tool {...} */ header"; return info; }
            try
            {
                var h = JObject.Parse(m.Groups[1].Value);
                info.Description = (string)h["description"] ?? "";
                if (h["inputSchema"] is JObject schema) info.InputSchema = schema;
                if (h["timeoutSeconds"] != null) info.TimeoutSeconds = (int)h["timeoutSeconds"];
                if (h["readOnly"] != null) info.ReadOnly = (bool)h["readOnly"];
                if (h["references"] is JArray refs) info.References = refs.Select(r => (string)r).Where(r => !string.IsNullOrEmpty(r)).ToList();
            }
            catch (Exception ex) { info.HeaderError = "header JSON: " + ex.Message; }
            return info;
        }

        /// <summary>Every *.cs of the commands folder with a valid name.</summary>
        public static List<Info> Scan()
        {
            var list = new List<Info>();
            string dir = Paths.Commands;
            if (!Directory.Exists(dir)) return list;
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!NameRule.IsMatch(name)) continue;
                try
                {
                    var info = ReadHeader(name, file, File.ReadAllText(file, Encoding.UTF8));
                    info.Stamp = File.GetLastWriteTimeUtc(file);
                    list.Add(info);
                }
                catch (Exception ex) { Log.Write($"command {name}: {ex.Message}"); }
            }
            return list.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
        }

        // ---- compile + run ------------------------------------------------------------------

        class Compiled { public string Key; public MethodInfo Run; }
        static readonly Dictionary<string, Compiled> Cache = new Dictionary<string, Compiled>();
        static readonly object Gate = new object();

        /// <summary>Run a command on the API thread. source: run this text instead of the file (web app).</summary>
        public static object Run(UIApplication app, string name, string source, JObject args)
        {
            if (!NameRule.IsMatch(name ?? "")) throw new ArgumentException("bad command name: " + name);
            string file = Path.Combine(Paths.Commands, name + ".cs");
            if (source == null)
            {
                if (!File.Exists(file)) throw new FileNotFoundException($"command not found: {name} (in {Paths.Commands})");
                source = File.ReadAllText(file, Encoding.UTF8);
            }
            var method = Get(name, file, source);
            string argsJson = (args ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None);
            // the JObject type the command was compiled against (normally the same Newtonsoft as ours)
            var jobjectType = method.GetParameters()[1].ParameterType;
            object argObj = jobjectType == typeof(JObject)
                ? (object)JObject.Parse(argsJson)
                : jobjectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { argsJson });
            try { return method.Invoke(null, new[] { app, argObj }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        static MethodInfo Get(string name, string file, string source)
        {
            string key = Hash(source);
            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var c) && c.Key == key) return c.Run;
            }
            var header = ReadHeader(name, file, source);
            var refs = References(header.References);
            string asmName = "AiCuliCmd_" + name + "_" + key.Substring(0, 12);
            var image = CompilerHost.Compile(asmName, source, file, refs, out var errors);
            if (image == null)
                throw new CommandCompileException($"{name}.cs does not compile:\n" + string.Join("\n", errors));
            var asm = Assembly.Load(image);
            var run = asm.GetTypes()
                .Select(t => t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static))
                .FirstOrDefault(m => m != null && m.GetParameters().Length == 2
                                     && m.GetParameters()[0].ParameterType == typeof(UIApplication));
            if (run == null)
                throw new CommandCompileException($"{name}.cs has no public static object Run(UIApplication app, JObject args)");
            lock (Gate) Cache[name] = new Compiled { Key = key, Run = run };
            Log.Write($"compiled {name}");
            return run;
        }

        static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// What a command may use: our Newtonsoft and AiCuli first (so the JObject it gets is ours), then the
        /// framework (.NET 8+: the trusted platform assemblies) and every assembly loaded in Revit, then the
        /// header's "references" (a loaded assembly name, or a .dll path / name in the commands folder).
        /// </summary>
        static string[] References(List<string> extra)
        {
            var list = new List<string>
            {
                typeof(JObject).Assembly.Location,
                typeof(DynamicCommands).Assembly.Location,
                typeof(Autodesk.Revit.DB.Document).Assembly.Location,
                typeof(UIApplication).Assembly.Location,
            };
            foreach (string n in new[] { "System.Core", "Microsoft.CSharp", "System.Xml.Linq", "System.Drawing", "System.Xaml",
                                         "WindowsBase", "PresentationCore", "PresentationFramework", "System.Windows.Forms",
                                         "System.Runtime", "netstandard" })
            {
                try { Assembly.Load(n); } catch { /* not on this framework */ }
            }
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
                list.AddRange(tpa.Split(Path.PathSeparator));
            else
            {
                // .NET Framework (Revit 2023-2024): the framework folder, facades included
                string fw = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                foreach (string n in new[] { "mscorlib", "System", "System.Core", "Microsoft.CSharp", "System.Xml", "System.Xml.Linq",
                                             "System.Data", "System.Drawing", "System.Windows.Forms", "System.Xaml", "WindowsBase",
                                             "PresentationCore", "PresentationFramework", "System.Net.Http", "System.Numerics",
                                             "netstandard", "System.Runtime", "System.Collections", "System.Linq" })
                {
                    foreach (string cand in new[] { Path.Combine(fw, n + ".dll"), Path.Combine(fw, "WPF", n + ".dll"),
                                                    Path.Combine(fw, "Facades", n + ".dll") })
                        if (File.Exists(cand)) { list.Add(cand); break; }
                }
            }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                string loc;
                try { loc = a.Location; } catch { continue; }
                if (string.IsNullOrEmpty(loc) || loc.StartsWith(Paths.RoslynDir, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(loc);
            }
            foreach (string r in extra)
            {
                var hit = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, r, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { list.Insert(0, hit.Location); continue; }
                foreach (string cand in new[] { r, Path.Combine(Paths.Commands, r), Path.Combine(Paths.Commands, r + ".dll"),
                                                Path.Combine(Paths.AddinDir, r + ".dll") })
                    if (File.Exists(cand)) { list.Insert(0, Path.GetFullPath(cand)); break; }
            }
            return list.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        }
    }

    public class CommandCompileException : Exception
    {
        public CommandCompileException(string message) : base(message) { }
    }
}
