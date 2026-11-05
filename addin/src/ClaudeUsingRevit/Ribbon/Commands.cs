using System;
using System.Linq;
using System.Windows.Input;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Python;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ClaudeUsingRevit.Ribbon
{
    public class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selected) => true;
    }

    /// <summary>
    /// A ribbon button of the extension: runs its script.py (Revit gives a command no way to know which
    /// button called it, so each button gets its own slot class, Cmd00..Cmd63, see Slots.cs).
    /// Shift+click sets EXEC_PARAMS.config_mode / __shiftclick__ as pyRevit did.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public abstract class ScriptCommand : IExternalCommand
    {
        readonly int _slot;
        protected ScriptCommand(int slot) { _slot = slot; }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var button = RibbonBuilder.Buttons.FirstOrDefault(b => b.Slot == _slot);
            if (button == null) return Result.Failed;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            string error;
            try { error = PyHost.RunButton(button, data.Application, shift); }
            catch (Exception ex) { error = ex.ToString(); }
            if (error != null)
            {
                Log.Write($"button {button.Name}: {error}");
                var td = new TaskDialog("claude using revit")
                {
                    MainInstruction = button.Title.Replace("\n", " ") + ": lỗi",
                    MainContent = LastLine(error),
                    ExpandedContent = error.Length > 3000 ? error.Substring(error.Length - 3000) : error,
                };
                td.Show();
            }
            return Result.Succeeded;
        }

        static string LastLine(string text)
        {
            var lines = text.Trim().Split('\n');
            return lines[lines.Length - 1].Trim();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class StatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Ui.StatusWindow.ShowOrActivate(data.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            string err = PyHost.RunStartup(data.Application);
            Mcp.ToolRegistry.Invalidate();
            if (err == null)
                TaskDialog.Show("claude using revit", "Đã tải lại phần Python (startup.py) và danh sách lệnh MCP.\n" +
                                            "Nút mới (thư mục *.pushbutton mới) cần mở lại Revit.");
            else
                new TaskDialog("claude using revit") { MainInstruction = "startup.py: lỗi", MainContent = err.Trim().Split('\n').Last(),
                                            ExpandedContent = err }.Show();
            return Result.Succeeded;
        }
    }
}
