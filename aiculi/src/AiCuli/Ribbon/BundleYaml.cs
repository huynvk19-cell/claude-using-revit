using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AiCuli.Ribbon
{
    /// <summary>
    /// The small part of YAML that bundle.yaml files use (same files as under pyRevit):
    ///   title: "Xuất\nPDF"          plain or quoted scalars ("\n" = new line)
    ///   tooltip: >-                 folded block (lines joined by spaces, blank line = new line)
    ///   layout:                     list of names
    ///     - Print Set Sync
    /// </summary>
    public class BundleYaml
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> Lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public List<string> GetList(string key) => Lists.TryGetValue(key, out var v) ? v : new List<string>();

        public static BundleYaml Load(string dir)
        {
            var y = new BundleYaml();
            string file = Path.Combine(dir, "bundle.yaml");
            if (!File.Exists(file)) return y;
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#") || char.IsWhiteSpace(line[0])) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim();
                string rest = line.Substring(colon + 1).Trim();
                if (rest.StartsWith(">") || rest.StartsWith("|"))
                {
                    bool folded = rest.StartsWith(">");
                    var block = new List<string>();
                    while (i + 1 < lines.Length && (lines[i + 1].Trim().Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                        block.Add(lines[++i].Trim());
                    while (block.Count > 0 && block[block.Count - 1].Length == 0) block.RemoveAt(block.Count - 1);
                    y.Values[key] = folded ? Fold(block) : string.Join("\n", block);
                }
                else if (rest.Length == 0)
                {
                    var items = new List<string>();
                    while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("- "))
                        items.Add(Scalar(lines[++i].TrimStart().Substring(2).Trim()));
                    y.Lists[key] = items;
                }
                else y.Values[key] = Scalar(rest);
            }
            return y;
        }

        static string Fold(List<string> block)
        {
            var sb = new StringBuilder();
            foreach (string l in block)
            {
                if (l.Length == 0) { sb.Append('\n'); continue; }
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append(' ');
                sb.Append(l);
            }
            return sb.ToString();
        }

        static string Scalar(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            int hash = s.IndexOf(" #", StringComparison.Ordinal);
            return hash >= 0 ? s.Substring(0, hash).TrimEnd() : s;
        }
    }
}
