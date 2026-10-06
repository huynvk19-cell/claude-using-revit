/* mcp-tool
{
  "description": "Smoke test for the dynamic command pipeline: returns Revit version, open documents and the active view. Edit this file and call it again to see changes apply with no restart.",
  "inputSchema": { "type": "object", "properties": {} },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class HelloRevit
{
    public static object Run(UIApplication app, JObject args)
    {
        var uidoc = app.ActiveUIDocument;
        return new
        {
            Revit = $"{app.Application.VersionName} ({app.Application.VersionBuild})",
            Runtime = System.Environment.Version.ToString(),
            Username = app.Application.Username,
            OpenDocuments = app.Application.Documents.Cast<Document>()
                .Select(d => new { d.Title, d.IsWorkshared, d.IsLinked, d.IsModelInCloud })
                .ToList(),
            ActiveDocument = uidoc?.Document.Title,
            ActiveView = uidoc?.ActiveView?.Name
        };
    }
}
