using System;
using AiCuli.Core;
using AiCuli.Mcp;
using AiCuli.Python;
using AiCuli.Ribbon;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace AiCuli
{
    /// <summary>
    /// AI Culi: one Revit add-in that is at the same time
    ///   - the ribbon tab of the tools (Python scripts of aiculi/extension, run by IronPython inside the add-in),
    ///   - the MCP server Claude talks to (http://127.0.0.1:8970/mcp; tools = revit-commands/*.cs + built-ins),
    ///   - its own status button (panel Claude: connected / waiting / another window / off).
    /// Needs neither pyRevit nor another Revit MCP add-in.
    /// </summary>
    public class App : IExternalApplication
    {
        public static UIApplication UIApp { get; private set; }
        public static string RevitVersion { get; private set; }

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                AssemblyResolver.Install();
                RevitVersion = app.ControlledApplication.VersionNumber;
                Log.Write($"AI Culi {McpServer.Version} starting in Revit {RevitVersion} ({app.ControlledApplication.VersionBuild})");
                RevitQueue.Init();
                RibbonBuilder.Build(app);
                Ui.StatusUpdater.Start();
                var ctl = app.ControlledApplication;
                ctl.ApplicationInitialized += OnInitialized;
                ctl.DocumentOpened += (s, e) => Hook("doc-opened", e);
                ctl.DocumentClosing += (s, e) => Hook("doc-closing", e);
                ctl.DocumentClosed += (s, e) => Hook("doc-closed", e);
                ToolRegistry.Watch();
                if (Config.Current.AutoStart) McpServer.Start();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("startup", ex);
                return Result.Failed;
            }
        }

        static void OnInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            try
            {
                UIApp = new UIApplication((Application)sender);
                PyHost.RunStartup(UIApp);
            }
            catch (Exception ex) { Log.Error("python startup", ex); }
        }

        static void Hook(string name, object args)
        {
            try { PyHost.RunHook(name, UIApp, args); }
            catch (Exception ex) { Log.Error("hook " + name, ex); }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { McpServer.Shutdown(); } catch { }
            Log.Write("AI Culi stopped");
            return Result.Succeeded;
        }
    }
}
