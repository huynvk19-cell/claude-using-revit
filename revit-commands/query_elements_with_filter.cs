/* mcp-tool
{
  "description": "Read-only: elements of a category (in a view or the model), filtered by parameter values.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "category": { "type": "string", "description": "Doors, Walls, Rooms... or OST_Doors" },
      "viewId": { "type": "number", "description": "Only elements visible in this view" },
      "filters": {
        "type": "array",
        "description": "All must match",
        "items": {
          "type": "object",
          "properties": {
            "field": { "type": "string", "description": "Parameter name (instance, then type)" },
            "operator": { "type": "string", "enum": ["equals", "not_equals", "contains", "less_than", "greater_than"] },
            "value": { "type": "string" }
          },
          "required": ["field", "operator", "value"]
        }
      },
      "returnFields": { "type": "array", "items": { "type": "string" }, "description": "Parameters to return" },
      "maxCount": { "type": "number", "description": "Default 100" }
    },
    "required": ["category"]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// category: the English category name as Revit shows it (Doors, Windows, Walls, Rooms, Floors, Columns, Structural
// Columns, Generic Models...), or the BuiltInCategory name (OST_Doors). Instances only (no types).
// viewId: FilteredElementCollector(doc, viewId) = what the view draws (crop + view range + visibility).
// filters: compared on the parameter's display string (AsValueString, else AsString); less_than / greater_than compare
// numbers in the display units (e.g. mm) when both sides parse as numbers. contains is case-insensitive.
// Each element: ElementId, Name, Type (type name), Level (when it has one) + the returnFields ("N/A" when missing).
// Total = elements matching before maxCount.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class QueryElementsWithFilter
{
    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        string catName = (string)args["category"];
        Category cat = FindCategory(doc, catName);
        if (cat == null) return new { error = "unknown category: " + catName };
        int max = args.Value<int?>("maxCount") ?? 100;
        var filters = (args["filters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var fields = (args["returnFields"] as JArray ?? new JArray()).Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList();

        FilteredElementCollector col = args["viewId"] != null
            ? new FilteredElementCollector(doc, new ElementId((int)args["viewId"]))
            : new FilteredElementCollector(doc);
        var found = new List<Dictionary<string, object>>();
        int total = 0;
        foreach (Element e in col.OfCategoryId(cat.Id).WhereElementIsNotElementType())
        {
            if (!filters.All(f => Match(doc, e, (string)f["field"], (string)f["operator"], (string)f["value"]))) continue;
            total++;
            if (found.Count >= max) continue;
            var item = new Dictionary<string, object> { ["ElementId"] = e.Id.IntegerValue, ["Name"] = e.Name ?? "" };
            Element type = doc.GetElement(e.GetTypeId());
            if (type != null) item["Type"] = type.Name;
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId && doc.GetElement(e.LevelId) is Level lv) item["Level"] = lv.Name;
            foreach (string f in fields)
                if (!item.ContainsKey(f)) item[f] = Text(Param(doc, e, f)) ?? "N/A";
            found.Add(item);
        }
        return new { Category = cat.Name, Total = total, Count = found.Count, Elements = found };
    }

    static Category FindCategory(Document doc, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(name, true, out BuiltInCategory bic))
        {
            try { return Category.GetCategory(doc, bic); } catch { return null; }
        }
        foreach (Category c in doc.Settings.Categories)
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        if (Enum.TryParse("OST_" + name.Replace(" ", ""), true, out BuiltInCategory b2))
        {
            try { return Category.GetCategory(doc, b2); } catch { }
        }
        return null;
    }

    static Parameter Param(Document doc, Element e, string name)
    {
        Parameter p = e.LookupParameter(name);
        if (p != null) return p;
        Element type = doc.GetElement(e.GetTypeId());
        return type?.LookupParameter(name);
    }

    static string Text(Parameter p)
    {
        if (p == null) return null;
        if (p.StorageType == StorageType.String) return p.AsString() ?? "";
        return p.AsValueString() ?? p.AsString() ?? "";
    }

    static bool Match(Document doc, Element e, string field, string op, string value)
    {
        string have = field == "Name" ? e.Name : Text(Param(doc, e, field));
        if (have == null) return op == "not_equals";
        value = value ?? "";
        switch (op)
        {
            case "equals": return string.Equals(have, value, StringComparison.OrdinalIgnoreCase);
            case "not_equals": return !string.Equals(have, value, StringComparison.OrdinalIgnoreCase);
            case "contains": return have.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
            case "less_than": return Num(have, out double a) && Num(value, out double b) && a < b;
            case "greater_than": return Num(have, out double c) && Num(value, out double d) && c > d;
            default: return false;
        }
    }

    static bool Num(string s, out double v)
    {
        string digits = new string((s ?? "").Trim().TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == ',').ToArray());
        return double.TryParse(digits.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
