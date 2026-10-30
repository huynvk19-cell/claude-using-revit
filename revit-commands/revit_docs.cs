/* mcp-tool
{
  "description": "Documents of the Revit window holding MCP: list (unsaved?, active, MCP client), open (cloud GUID or path), close (refuses unsaved).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": { "type": "string", "enum": ["list", "open", "close"], "description": "Default list" },
      "title": { "type": "string", "description": "close: exact document title" },
      "switchTo": { "type": "string", "description": "close of the active doc: title of the open doc to activate first" },
      "discardChanges": { "type": "boolean", "description": "close: allow closing with unsaved changes (only on user OK)" },
      "path": { "type": "string", "description": "open: local/server file path" },
      "region": { "type": "string", "description": "open cloud: region, default US" },
      "projectGuid": { "type": "string", "description": "open cloud: project GUID" },
      "modelGuid": { "type": "string", "description": "open cloud: model GUID" },
      "activate": { "type": "boolean", "description": "open: show in the UI (default true); false = background" }
    }
  },
  "timeoutSeconds": 1800,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Model open/close for the Revit window that currently holds the MCP port (only that window can be reached).
//  list: Revit PID + window, the MCP client holding the lock (name, remote endpoint: its local port maps to the node
//    bridge PID via Get-NetTCPConnection -LocalPort), every non-linked document with IsModified / Active / path, and
//    the number of loaded links. Read-only.
//  open: cloud model (projectGuid + modelGuid [+ region]) or local path. activate=true (default) opens it in the UI
//    and makes it active (OpenAndActivateDocument); false opens it in the background (closes again with close).
//    Never detaches from central. Never syncs. Always opens ALL worksets (closed worksets print views incomplete).
//    Never opens a model twice: open in this window -> only activated; open in ANOTHER Revit window -> refused with
//    that window's PID (work there instead). The other windows' documents come from
//    %APPDATA%\claude-using-revit\open-docs\<pid>.json, written by the pyRevit extension (startup + doc open/close
//    hooks) and by this command for its own window. A live Revit window without that file has unknown documents, so
//    open is refused until it has one (pyRevit Reload there, or run revit_docs list while it holds the port).
//  close: closes the non-linked document whose Title equals title. Refuses when it has unsaved changes unless
//    discardChanges=true (ask the user first; changes are lost). The API cannot close the active document: pass
//    switchTo (another open document's title) to activate that one first. The last open document cannot be closed
//    through the API: close it in the UI or close the Revit window.
//  Dialogs Revit shows meanwhile are reported (DialogsSeen) but not answered.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Newtonsoft.Json.Linq;

public static class RevitDocs
{
    public static object Run(UIApplication app, JObject args)
    {
        string action = args.Value<string>("action") ?? "list";
        var dialogs = new List<string>();
        EventHandler<DialogBoxShowingEventArgs> onDialog = (s, e) => dialogs.Add(e.DialogId);
        app.DialogBoxShowing += onDialog;
        try
        {
            WriteSnapshot(app);
            object result;
            if (action == "open") result = Open(app, args);
            else if (action == "close") result = Close(app, args);
            else result = null;
            if (result != null) WriteSnapshot(app);
            return new { Action = action, Result = result, DialogsSeen = dialogs.Count > 0 ? dialogs : null, State = List(app) };
        }
        finally
        {
            app.DialogBoxShowing -= onDialog;
        }
    }

    static string SnapshotFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "claude-using-revit", "open-docs");

    // Same file and shape as pyrevit lib/open_docs.py.
    static void WriteSnapshot(UIApplication app)
    {
        try
        {
            var p = Process.GetCurrentProcess();
            Document active = app.ActiveUIDocument?.Document;
            var docs = new JArray();
            foreach (Document d in app.Application.Documents)
            {
                if (d.IsLinked) continue;
                docs.Add(new JObject
                {
                    ["title"] = d.Title,
                    ["path"] = PathOf(d),
                    ["modelGuid"] = GuidOf(d),
                    ["modified"] = d.IsModified,
                    ["active"] = active != null && d.Equals(active)
                });
            }
            var state = new JObject
            {
                ["pid"] = p.Id,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o"),
                ["source"] = "revit_docs",
                ["window"] = p.MainWindowTitle,
                ["docs"] = docs
            };
            System.IO.Directory.CreateDirectory(SnapshotFolder);
            string target = System.IO.Path.Combine(SnapshotFolder, p.Id + ".json");
            System.IO.File.WriteAllText(target + ".tmp", state.ToString());
            if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
            System.IO.File.Move(target + ".tmp", target);
        }
        catch { }
    }

    static string GuidOf(Document d)
    {
        try { return d.IsModelInCloud ? d.GetCloudModelPath().GetModelGUID().ToString() : ""; }
        catch { return ""; }
    }

    // Who else has this model open? Returns a refusal reason, or null when it is safe to open here.
    static string OpenElsewhere(string modelGuid, string path)
    {
        int self = Process.GetCurrentProcess().Id;
        var unknown = new List<string>();
        foreach (var rp in Process.GetProcessesByName("Revit"))
        {
            if (rp.Id == self) continue;
            string file = System.IO.Path.Combine(SnapshotFolder, rp.Id + ".json");
            if (!System.IO.File.Exists(file)) { unknown.Add($"PID {rp.Id} ({rp.MainWindowTitle})"); continue; }
            JObject st;
            try { st = JObject.Parse(System.IO.File.ReadAllText(file)); }
            catch { unknown.Add($"PID {rp.Id} (unreadable open-docs file)"); continue; }
            foreach (JObject d in (st["docs"] as JArray ?? new JArray()).OfType<JObject>())
            {
                bool same = !string.IsNullOrEmpty(modelGuid)
                    ? string.Equals((string)d["modelGuid"], modelGuid, StringComparison.OrdinalIgnoreCase)
                    : string.Equals((string)d["path"], path, StringComparison.OrdinalIgnoreCase);
                if (same) return $"already open in Revit window PID {rp.Id} as '{d["title"]}': work there, do not open it again";
            }
        }
        if (unknown.Count > 0)
            return "documents of these Revit windows are unknown (no open-docs file; pyRevit Reload there or revit_docs list while it holds the port): "
                + string.Join("; ", unknown);
        return null;
    }

    static object List(UIApplication app)
    {
        var p = Process.GetCurrentProcess();
        Document active = app.ActiveUIDocument?.Document;
        var all = app.Application.Documents.Cast<Document>().ToList();
        object client = null;
        try
        {
            var svc = RevitMCP.Application.SocketService;
            if (svc != null)
            {
                var st = svc.GetStatusSnapshot();
                client = new { svc.IsRunning, Locked = st.locked, Name = st.clientName, Remote = st.remote, SinceUtc = st.sinceUtc };
            }
        }
        catch (Exception ex) { client = "unavailable: " + ex.Message; }

        return new
        {
            RevitPid = p.Id,
            Window = p.MainWindowTitle,
            app.Application.Username,
            McpClient = client,
            Documents = all.Where(d => !d.IsLinked).Select(d => new
            {
                d.Title,
                Active = active != null && d.Equals(active),
                d.IsModified,
                d.IsWorkshared,
                d.IsModelInCloud,
                d.IsFamilyDocument,
                Path = PathOf(d)
            }).ToList(),
            LinkedLoaded = all.Count(d => d.IsLinked)
        };
    }

    static string PathOf(Document d)
    {
        try
        {
            if (d.IsModelInCloud)
            {
                ModelPath cp = d.GetCloudModelPath();
                return $"CLD://{cp.Region}/{cp.GetProjectGUID()}/{cp.GetModelGUID()}";
            }
            return d.PathName;
        }
        catch { return d.PathName; }
    }

    static object Open(UIApplication app, JObject args)
    {
        ModelPath mp;
        string path = args.Value<string>("path");
        string modelGuid = null;
        if (!string.IsNullOrEmpty(path))
            mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        else
        {
            string region = args.Value<string>("region") ?? ModelPathUtils.CloudRegionUS;
            Guid project = Guid.Parse(args.Value<string>("projectGuid") ?? throw new ArgumentException("projectGuid or path is required"));
            Guid model = Guid.Parse(args.Value<string>("modelGuid") ?? throw new ArgumentException("modelGuid is required"));
            modelGuid = model.ToString();
            mp = ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, project, model);
        }

        // Already open in this window: activate it, never open it again.
        Document mine = app.Application.Documents.Cast<Document>().FirstOrDefault(d => !d.IsLinked &&
            (modelGuid != null ? string.Equals(GuidOf(d), modelGuid, StringComparison.OrdinalIgnoreCase)
                               : string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase)));
        if (mine != null)
        {
            bool isActive = app.ActiveUIDocument != null && app.ActiveUIDocument.Document.Equals(mine);
            if (!isActive && (args.Value<bool?>("activate") ?? true))
                app.OpenAndActivateDocument(mine.IsModelInCloud ? mine.GetCloudModelPath() : mp, new OpenOptions(), false);
            return new { Opened = (string)null, AlreadyOpen = mine.Title, Active = isActive || (args.Value<bool?>("activate") ?? true) };
        }

        string refuse = OpenElsewhere(modelGuid, path);
        if (refuse != null) return new { Opened = (string)null, Refused = refuse };

        var opts = new OpenOptions { DetachFromCentralOption = DetachFromCentralOption.DoNotDetach };
        opts.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));

        var sw = Stopwatch.StartNew();
        if (args.Value<bool?>("activate") ?? true)
        {
            UIDocument ui = app.OpenAndActivateDocument(mp, opts, false);
            return new { Opened = ui.Document.Title, Active = true, Ms = sw.ElapsedMilliseconds };
        }
        Document doc = app.Application.OpenDocumentFile(mp, opts);
        return new { Opened = doc.Title, Active = false, Ms = sw.ElapsedMilliseconds };
    }

    static object Close(UIApplication app, JObject args)
    {
        string title = args.Value<string>("title") ?? throw new ArgumentException("title is required");
        var docs = app.Application.Documents.Cast<Document>().Where(d => !d.IsLinked).ToList();
        Document doc = docs.FirstOrDefault(d => d.Title == title);
        if (doc == null) return new { Closed = false, Reason = "no open (non-linked) document titled " + title };
        if (doc.IsModified && args.Value<bool?>("discardChanges") != true)
            return new { Closed = false, Reason = "unsaved changes: save/sync first, or discardChanges=true after the user agrees" };

        Document active = app.ActiveUIDocument?.Document;
        if (active != null && doc.Equals(active))
        {
            string switchTo = args.Value<string>("switchTo");
            Document other = docs.FirstOrDefault(d => d.Title == switchTo && !d.Equals(doc));
            if (other == null)
                return new
                {
                    Closed = false,
                    Reason = docs.Count > 1
                        ? "active document: pass switchTo = title of another open document"
                        : "last open document: close it in the UI or close this Revit window"
                };
            ModelPath op = other.IsModelInCloud ? other.GetCloudModelPath() : ModelPathUtils.ConvertUserVisiblePathToModelPath(other.PathName);
            app.OpenAndActivateDocument(op, new OpenOptions(), false);
        }

        doc.Close(false);
        return new { Closed = true, Title = title };
    }
}
