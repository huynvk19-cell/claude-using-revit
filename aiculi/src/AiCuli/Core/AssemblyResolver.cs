using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AiCuli.Core
{
    /// <summary>
    /// Revit add-ins have no app.config (no binding redirects). When a load fails (an IronPython or
    /// compiled-command reference), answer with: a loaded assembly of that name and at least that version,
    /// else the file in the add-in folder, else a loaded one of any version.
    /// </summary>
    public static class AssemblyResolver
    {
        static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            AssemblyName want;
            try { want = new AssemblyName(args.Name); } catch { return null; }
            if (want.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && string.Equals(a.GetName().Name, want.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.GetName().Version)
                .ToList();
            var good = loaded.FirstOrDefault(a => want.Version == null || a.GetName().Version >= want.Version);
            if (good != null) return good;
            foreach (string dir in new[] { Paths.AddinDir })
            {
                string file = Path.Combine(dir, want.Name + ".dll");
                if (File.Exists(file))
                {
                    try { return Assembly.LoadFrom(file); } catch { }
                }
            }
            return loaded.FirstOrDefault();
        }
    }
}
