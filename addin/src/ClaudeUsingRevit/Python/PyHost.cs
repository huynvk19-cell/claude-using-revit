using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClaudeUsingRevit.Core;
using Autodesk.Revit.UI;
using IronPython.Hosting;
using IronPython.Runtime.Exceptions;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace ClaudeUsingRevit.Python
{
    /// <summary>
    /// What a Python script of the extension sees as the builtin __cur__ (read by lib/cur/*.py, the
    /// replacement of the pyRevit API the tools used: HOST_APP, EXEC_PARAMS, forms, revit, script).
    /// </summary>
    public class HostContext
    {
        public UIApplication UIApp { get; set; }
        public bool ShiftClick { get; set; }
        public string ScriptPath { get; set; }
        public string BundleDir { get; set; }
        public string BundleName { get; set; }
        public object EventArgs { get; set; }
        public string ExtensionDir => Paths.Extension;
        public string DataDir => Paths.Data;
        public string ToolConfigDir => Paths.ToolConfig;
        public long MainWindowHandle => UIApp?.MainWindowHandle.ToInt64() ?? 0;
        public double ScreenScale => Ui.Dpi.Scale;
        public string Version => typeof(HostContext).Assembly.GetName().Version.ToString();
    }

    /// <summary>
    /// IronPython 2.7 for the extension (addin/extension). Two kinds of engine, as pyRevit had:
    ///  - the startup engine: lives as long as Revit, runs startup.py (web bridge, the Bảng window) and the
    ///    document hooks (hooks/*.py);
    ///  - a fresh engine per ribbon click and per cur_task call, so edits of the Python files apply on the
    ///    next click without restarting Revit.
    /// </summary>
    public static class PyHost
    {
        static ScriptEngine _startup;
        static readonly HostContext StartupContext = new HostContext();

        public static ScriptEngine NewEngine(HostContext ctx)
        {
            var options = new Dictionary<string, object> { ["Frames"] = true, ["FullFrames"] = true };
            var engine = IronPython.Hosting.Python.CreateEngine(options);
            var paths = engine.GetSearchPaths().ToList();
            foreach (string p in new[] { Paths.ExtensionLib, Paths.StdLib, Path.Combine(Paths.StdLib, "site-packages") })
                if (!paths.Contains(p)) paths.Add(p);
            engine.SetSearchPaths(paths);
            engine.Runtime.LoadAssembly(typeof(Autodesk.Revit.DB.Document).Assembly);
            engine.Runtime.LoadAssembly(typeof(UIApplication).Assembly);
            engine.Runtime.LoadAssembly(typeof(PyHost).Assembly);
            LoadIfPresent(engine, "IronPython.Wpf");
            var builtins = engine.GetBuiltinModule();
            builtins.SetVariable("__cur__", ctx);
            builtins.SetVariable("__revit__", ctx.UIApp);
            builtins.SetVariable("__shiftclick__", ctx.ShiftClick);
            return engine;
        }

        static void LoadIfPresent(ScriptEngine engine, string name)
        {
            string file = Path.Combine(Paths.AddinDir, name + ".dll");
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
                          ?? (File.Exists(file) ? Assembly.LoadFrom(file) : null);
                if (asm != null) engine.Runtime.LoadAssembly(asm);
            }
            catch (Exception ex) { Log.Write($"python: {name} not loaded: {ex.Message}"); }
        }

        /// <summary>Run a script file in an engine. Returns null on success, else the Python traceback.</summary>
        public static string RunFile(ScriptEngine engine, string path, ScriptScope scope = null)
        {
            scope = scope ?? engine.CreateScope();
            scope.SetVariable("__file__", path);
            scope.SetVariable("__name__", "__main__");
            try
            {
                engine.CreateScriptSourceFromFile(path, Encoding.UTF8, SourceCodeKind.File).Execute(scope);
                return null;
            }
            catch (SystemExitException) { return null; }          // script.exit() / sys.exit()
            catch (Exception ex) { return Format(engine, ex); }
        }

        public static string Format(ScriptEngine engine, Exception ex)
        {
            try { return engine.GetService<ExceptionOperations>().FormatException(ex); }
            catch { return ex.ToString(); }
        }

        // ---- startup engine -------------------------------------------------------------------

        /// <summary>(Re)create the startup engine and run startup.py. The Tải lại button calls it again.</summary>
        public static string RunStartup(UIApplication uiapp)
        {
            StartupContext.UIApp = uiapp;
            string script = Path.Combine(Paths.Extension, "startup.py");
            if (!File.Exists(script)) return "startup.py not found in " + Paths.Extension;
            _startup = NewEngine(StartupContext);
            string err = RunFile(_startup, script);
            if (err != null) Log.Write("startup.py: " + err);
            return err;
        }

        /// <summary>hooks/&lt;name&gt;.py in the startup engine (doc-opened, doc-closing, doc-closed).</summary>
        public static void RunHook(string name, UIApplication uiapp, object eventArgs)
        {
            string script = Path.Combine(Paths.Extension, "hooks", name + ".py");
            if (_startup == null || !File.Exists(script)) return;
            StartupContext.UIApp = uiapp ?? StartupContext.UIApp;
            StartupContext.EventArgs = eventArgs;
            try
            {
                string err = RunFile(_startup, script);
                if (err != null) Log.Write($"hook {name}: {err}");
            }
            finally { StartupContext.EventArgs = null; }
        }

        // ---- ribbon buttons -------------------------------------------------------------------

        public static string RunButton(Ribbon.ButtonInfo button, UIApplication uiapp, bool shift)
        {
            var ctx = new HostContext
            {
                UIApp = uiapp,
                ShiftClick = shift,
                ScriptPath = button.Script,
                BundleDir = button.Dir,
                BundleName = button.Name,
            };
            var engine = NewEngine(ctx);
            return RunFile(engine, button.Script);
        }

        // ---- shared tasks (lib/tasks.py) ------------------------------------------------------

        /// <summary>tasks.run_json(name, args) in a fresh engine; JSON text back ({"error": ...} on failure).</summary>
        public static string RunTask(UIApplication uiapp, string task, string argsJson)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (!File.Exists(Path.Combine(Paths.ExtensionLib, "tasks.py")))
                return Json.Error("tasks.py not found in " + Paths.ExtensionLib + " (install.ps1)");
            var engine = NewEngine(new HostContext { UIApp = uiapp });
            var scope = engine.CreateScope();
            scope.SetVariable("doc", uidoc?.Document);
            scope.SetVariable("uidoc", uidoc);
            scope.SetVariable("task_name", string.IsNullOrEmpty(task) ? "list_tasks" : task);
            scope.SetVariable("task_args", string.IsNullOrEmpty(argsJson) ? "{}" : argsJson);
            try
            {
                engine.Execute("import tasks\nresult = tasks.run_json(task_name, task_args, doc, uidoc)", scope);
                return scope.GetVariable<string>("result");
            }
            catch (Exception ex)
            {
                string trace = Format(engine, ex);
                return Json.Error(ex.Message, trace.Length > 1500 ? trace.Substring(trace.Length - 1500) : trace);
            }
        }
    }
}
