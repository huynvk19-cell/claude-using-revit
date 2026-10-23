/* mcp-tool
{
  "description": "Run a shared task of the pyRevit tab 'claude using revit' (same code as its buttons). task list_tasks lists tasks + args. Prefer this over re-deriving a job.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "task": { "type": "string", "description": "Task name, e.g. print_set_sync; list_tasks lists them" },
      "args": { "type": "object", "description": "Task args (see list_tasks); dryRun reports only" },
      "libDir": { "type": "string", "description": "Override the extension lib folder (testing a checkout)" }
    },
    "required": ["task"]
  },
  "references": ["pyRevitLabs.IronPython", "pyRevitLabs.Microsoft.Scripting", "pyRevitLabs.Microsoft.Dynamic"],
  "timeoutSeconds": 300
}
*/
// ---- Details
// Tasks live in pyrevit/ClaudeUsingRevit.extension/lib/tasks.py: one Python function per
// repeated job, also called by the ribbon buttons, so each job is written once. Adding a
// task needs no change here and no new MCP session.
// Runs the tasks in a fresh IronPython engine (pyRevit's own 2.7.12 engine assemblies,
// already loaded in Revit by pyRevit), so lib edits apply on the next call. The Python
// standard library is the zip pyRevit embeds in pyRevitLoader; it is copied once to
// %TEMP%\claude-using-revit\. Default lib folder = the installed extension
// (%APPDATA%\pyRevit\Extensions\ClaudeUsingRevit.extension\lib, install.ps1 -PyRevit).
// Args travel as JSON both ways; a Python error comes back as { error, trace }.
using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Newtonsoft.Json.Linq;

public static class PyRevitTask
{
    const string ExtLib = @"pyRevit\Extensions\ClaudeUsingRevit.extension\lib";

    public static object Run(UIApplication app, JObject args)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc == null) return new { error = "no open document" };
        string lib = (string)args["libDir"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ExtLib);
        if (!File.Exists(Path.Combine(lib, "tasks.py")))
            return new { error = "tasks.py not found in " + lib + " (install.ps1 -PyRevit)" };

        var engine = Python.CreateEngine();
        engine.SetSearchPaths(new[] { lib, StdLib() });
        engine.Runtime.LoadAssembly(typeof(Document).Assembly);
        engine.Runtime.LoadAssembly(typeof(UIApplication).Assembly);
        var scope = engine.CreateScope();
        scope.SetVariable("doc", uidoc.Document);
        scope.SetVariable("uidoc", uidoc);
        scope.SetVariable("task_name", (string)args["task"] ?? "list_tasks");
        scope.SetVariable("task_args", (args["args"] ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None));
        try
        {
            engine.Execute("import clr\nclr.AddReference('RevitAPI')\nclr.AddReference('RevitAPIUI')\n"
                + "import tasks\nresult = tasks.run_json(task_name, task_args, doc, uidoc)", scope);
        }
        catch (Exception e)
        {
            string trace = engine.GetService<ExceptionOperations>().FormatException(e);
            return new { error = e.Message, trace = trace.Length > 1500 ? trace.Substring(trace.Length - 1500) : trace };
        }
        return JToken.Parse(scope.GetVariable<string>("result"));
    }

    /// <summary>pyRevit's embedded IronPython standard library, copied to a zip on disk once.</summary>
    static string StdLib()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic && x.GetName().Name.StartsWith("pyRevit")))
        {
            string res = a.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("_lib.zip") && n.Contains("python_27"));
            if (res == null) continue;
            using (var s = a.GetManifestResourceStream(res))
            {
                string dir = Path.Combine(Path.GetTempPath(), "claude-using-revit");
                Directory.CreateDirectory(dir);
                string zip = Path.Combine(dir, res);
                if (!File.Exists(zip) || new FileInfo(zip).Length != s.Length)
                    using (var f = File.Create(zip)) s.CopyTo(f);
                return zip;
            }
        }
        throw new Exception("pyRevit IronPython standard library not found (is pyRevit loaded in this Revit?)");
    }
}
