using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiCuli.Compiler
{
    /// <summary>Roslyn behind a plain API (strings in, bytes out) so AI Culi can call it across load contexts.</summary>
    public static class Compiler
    {
        /// <summary>
        /// Compile one C# source file to a DLL image.
        /// Returns the bytes, or null with the error lines in <paramref name="errors"/>.
        /// </summary>
        public static byte[] Compile(string assemblyName, string source, string sourcePath, string[] referencePaths,
                                     out string[] errors)
        {
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Latest), sourcePath ?? "", Encoding.UTF8);
            var refs = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in referencePaths ?? new string[0])
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                if (!seen.Add(Path.GetFileName(p))) continue;     // one copy of each assembly
                try { refs.Add(MetadataReference.CreateFromFile(p)); }
                catch { /* not a .NET assembly */ }
            }
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                platform: Platform.X64,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
            var compilation = CSharpCompilation.Create(assemblyName, new[] { tree }, refs, options);
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    errors = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Take(20)
                        .Select(d =>
                        {
                            var pos = d.Location.GetLineSpan().StartLinePosition;
                            return $"line {pos.Line + 1}:{pos.Character + 1} {d.Id} {d.GetMessage()}";
                        })
                        .ToArray();
                    return null;
                }
                errors = new string[0];
                return ms.ToArray();
            }
        }
    }
}
