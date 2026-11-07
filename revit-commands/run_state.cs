/* mcp-tool
{
  "description": "Silent note of the running workflow step, for the add-in's Workflow window: set | clear.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": {
        "type": "string",
        "enum": [
          "set",
          "clear"
        ]
      },
      "message": { "type": "string", "description": "current step" },
      "step": { "type": "string", "description": "optional progress text, e.g. '3/12'" },
      "workflow": { "type": "string", "description": "running workflow file name, e.g. floor-plan" },
      "skill": { "type": "string", "description": "running skill, e.g. drafting-plan-dims" },
      "card": { "type": "string", "description": "running card number, e.g. 02" },
      "checklist": { "type": "string", "description": "the workflow checklist file (full path)" }
    },
    "required": [
      "action"
    ]
  },
  "timeoutSeconds": 30,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Writes %APPDATA%\claude-using-revit\run-state.json; shows nothing on screen. Read by lib/workflow_lib.py run_status(): the
//    Workflow window marks the running card, and the content update waits. set keeps a field it is not given; clear deletes
//    the file. A state left behind counts as ended after 6 hours (RUN_STALE_S).
using System;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RunState
{
    public static object Run(UIApplication app, JObject args)
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "claude-using-revit", "run-state.json");
        if ((string)args["action"] == "clear")
        {
            if (File.Exists(file)) File.Delete(file);
            return new { Cleared = true };
        }
        JObject o = new JObject();
        if (File.Exists(file)) { try { o = JObject.Parse(File.ReadAllText(file)); } catch { } }
        foreach (var key in new[] { "message", "step", "workflow", "skill", "card", "checklist" })
            if (args[key] != null) o[key] = (string)args[key];
        File.WriteAllText(file, o.ToString(), new System.Text.UTF8Encoding(false));
        return new { Set = true };
    }
}
