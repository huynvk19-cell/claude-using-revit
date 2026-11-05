using System;
using System.IO;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif
using ClaudeUsingRevit.Core;

namespace ClaudeUsingRevit.Mcp
{
    /// <summary>
    /// Loads ClaudeUsingRevit.Compiler.dll (Roslyn) from roslyn\ on first use. On .NET 8+ it gets its own
    /// AssemblyLoadContext, so a different Roslyn of another add-in in the same Revit does not matter.
    /// </summary>
    public static class CompilerHost
    {
        static MethodInfo _compile;
        static readonly object Gate = new object();

        static MethodInfo Method()
        {
            lock (Gate)
            {
                if (_compile != null) return _compile;
                string dll = Path.Combine(Paths.RoslynDir, "ClaudeUsingRevit.Compiler.dll");
                if (!File.Exists(dll)) throw new FileNotFoundException("C# compiler not installed: " + dll + " (build.ps1)");
#if NETFRAMEWORK
                var asm = Assembly.LoadFrom(dll);
#else
                var asm = new RoslynContext(Paths.RoslynDir).LoadFromAssemblyPath(dll);
#endif
                _compile = asm.GetType("ClaudeUsingRevit.Compiler.Compiler", true).GetMethod("Compile");
                return _compile;
            }
        }

        public static byte[] Compile(string assemblyName, string source, string sourcePath, string[] references,
                                     out string[] errors)
        {
            var args = new object[] { assemblyName, source, sourcePath, references, null };
            byte[] image;
            try { image = (byte[])Method().Invoke(null, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
            errors = (string[])args[4] ?? new string[0];
            return image;
        }

#if !NETFRAMEWORK
        class RoslynContext : AssemblyLoadContext
        {
            readonly string _dir;
            public RoslynContext(string dir) : base("ClaudeUsingRevit.Roslyn") { _dir = dir; }

            protected override Assembly Load(AssemblyName name)
            {
                string file = Path.Combine(_dir, name.Name + ".dll");
                return File.Exists(file) ? LoadFromAssemblyPath(file) : null;   // null: the framework's copy
            }
        }
#endif
    }
}
