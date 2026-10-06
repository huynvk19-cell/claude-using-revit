/* mcp-tool
{
  "description": "Small 'work in progress' window shown on top of every app (standalone Windows process, not tied to Revit). action show: open/replace with title + message; update: change message/step; close: close it. Neutral wording only. Same window as %USERPROFILE%\\Tools\\WorkStatus\\ws.ps1.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": { "type": "string", "enum": ["show", "update", "close"] },
      "title": { "type": "string", "description": "task name, e.g. 'Rà soát bản vẽ ES'" },
      "message": { "type": "string", "description": "current step" },
      "step": { "type": "string", "description": "optional progress text, e.g. '3/12'" }
    },
    "required": ["action"]
  },
  "timeoutSeconds": 30
}
*/
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class WorkStatus
{
    const string KEY = "WORK_STATUS_WINDOW";   // legacy in-Revit window

    public static object Run(UIApplication app, JObject args)
    {
        // close the old in-Revit window if one is still open
        var legacy = AppDomain.CurrentDomain.GetData(KEY) as Window;
        if (legacy != null) { try { legacy.Dispatcher.Invoke(() => legacy.Close()); } catch { } AppDomain.CurrentDomain.SetData(KEY, null); }

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tools", "WorkStatus");
        string file = Path.Combine(dir, "status.json"), pidFile = Path.Combine(dir, "host.pid");
        string action = (string)args["action"];
        JObject cur = null;
        if (File.Exists(file) && action != "show") { try { cur = JObject.Parse(File.ReadAllText(file)); } catch { } }

        if (action == "close")
        {
            if (File.Exists(file)) File.WriteAllText(file, new JObject { ["state"] = "close" }.ToString());
            return new { Closed = true };
        }
        var o = new JObject
        {
            ["title"] = (string)args["title"] ?? (string)cur?["title"] ?? "Đang xử lý",
            ["message"] = (string)args["message"] ?? (string)cur?["message"] ?? "",
            ["step"] = (string)args["step"] ?? (action == "update" ? (string)cur?["step"] : "") ?? "",
            ["state"] = "open"
        };
        File.WriteAllText(file, o.ToString(), new System.Text.UTF8Encoding(false));

        bool running = false;
        if (File.Exists(pidFile))
        {
            try { var p = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim())); running = p.ProcessName.StartsWith("powershell", StringComparison.OrdinalIgnoreCase); } catch { }
        }
        if (!running)
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(dir, "host.ps1") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi);
        }
        return new { Shown = true, Started = !running };
    }
}
