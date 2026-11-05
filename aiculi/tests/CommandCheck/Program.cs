using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

// dotnet run --project aiculi/tests/CommandCheck -- [revitVersion=2025] [commandsDir] [--strict]
//   Compiles each command with the reference set of aiculi/src/AiCuli/bin/Release/<version>/ (compile-refs.txt +
//   AiCuli.dll). Exit code 1 when a command does not compile or its header is broken; --strict also fails on
//   header style warnings.
static class Program
{
    static readonly Regex Header = new Regex(@"/\*\s*mcp-tool\s*([\s\S]*?)\*/");
    static readonly Regex RunSig = new Regex(@"public\s+static\s+object\s+Run\s*\(\s*UIApplication\s+\w+\s*,\s*JObject\s+\w+\s*\)");

    static int Main(string[] argv)
    {
        string root = FindRepoRoot();
        bool strict = argv.Contains("--strict");
        var pos = argv.Where(a => !a.StartsWith("--")).ToList();
        string version = pos.Count > 0 ? pos[0] : "2025";
        string commands = pos.Count > 1 ? pos[1] : Path.Combine(root, "revit-commands");
        string bin = Path.Combine(root, "aiculi", "src", "AiCuli", "bin", "Release", version);
        string refsFile = Path.Combine(bin, "compile-refs.txt");
        if (!File.Exists(refsFile))
        {
            Console.Error.WriteLine($"{refsFile} missing: dotnet build aiculi/src/AiCuli -c Release -p:RevitVersion={version}");
            return 2;
        }
        var refs = new List<string> { Path.Combine(bin, "AiCuli.dll") };
        refs.AddRange(File.ReadAllLines(refsFile).Where(l => l.Trim().Length > 0));

        int failed = 0, warned = 0, ok = 0;
        foreach (string file in Directory.GetFiles(commands, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string src = File.ReadAllText(file, Encoding.UTF8);
            var problems = new List<string>();
            var warnings = new List<string>();
            var m = Header.Match(src);
            if (!m.Success) problems.Add("no /* mcp-tool */ header");
            else
            {
                try { CheckHeader(JObject.Parse(m.Groups[1].Value), warnings); }
                catch (Exception ex) { problems.Add("header JSON: " + ex.Message); }
            }
            if (!RunSig.IsMatch(src)) problems.Add("no public static object Run(UIApplication app, JObject args)");
            var image = AiCuli.Compiler.Compiler.Compile("Check_" + name, src, file, refs.ToArray(), out string[] errors);
            if (image == null) problems.AddRange(errors);

            if (problems.Count > 0)
            {
                failed++;
                Console.WriteLine($"FAIL {name}");
                foreach (var p in problems) Console.WriteLine("     " + p);
            }
            else ok++;
            if (warnings.Count > 0)
            {
                warned++;
                Console.WriteLine($"warn {name}: " + string.Join("; ", warnings));
            }
        }
        Console.WriteLine($"Revit {version}: {ok} ok, {failed} failed, {warned} with header warnings ({commands})");
        return failed > 0 || (strict && warned > 0) ? 1 : 0;
    }

    static void CheckHeader(JObject h, List<string> warnings)
    {
        string desc = (string)h["description"] ?? "";
        if (desc.Length == 0) warnings.Add("empty description");
        if (desc.Length > 240) warnings.Add($"description {desc.Length} chars (keep 1-2 lines)");
        if (!(h["inputSchema"] is JObject)) warnings.Add("no inputSchema");
        foreach (var prop in h.SelectTokens("$..properties.*").OfType<JObject>())
        {
            string d = (string)prop["description"];
            if (d != null && d.Length > 80)
                warnings.Add($"param '{((JProperty)prop.Parent).Name}' description {d.Length} chars (> 80)");
        }
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "revit-commands"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
