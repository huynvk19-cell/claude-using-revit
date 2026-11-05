using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Python;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace ClaudeUsingRevit.Mcp
{
    public class ToolDef
    {
        public string Name;
        public string Description;
        public JObject InputSchema;
        public bool ReadOnly;
        public int TimeoutSeconds = 300;
        /// <summary>Runs on Revit's API thread.</summary>
        public Func<UIApplication, JObject, object> OnApi;
        /// <summary>Runs on the caller's thread (no Revit API; answers even while Revit is busy).</summary>
        public Func<JObject, object> OffApi;
        public bool BuiltIn;

        public JObject ToMcp()
        {
            var o = new JObject
            {
                ["name"] = Name,
                ["description"] = Description ?? "",
                ["inputSchema"] = InputSchema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() },
            };
            if (ReadOnly) o["annotations"] = new JObject { ["readOnlyHint"] = true };
            return o;
        }
    }

    /// <summary>
    /// The tools Claude sees: the built-in ones below + one per command file (DynamicCommands).
    /// The list is re-read when the commands folder changes (and Claude is told: tools/list_changed).
    /// </summary>
    public static class ToolRegistry
    {
        static readonly object Gate = new object();
        static List<ToolDef> _tools;
        static string _stamp;
        static FileSystemWatcher _watcher;
        static Timer _debounce;

        public static event Action Changed;

        static JObject Schema(params (string name, string type, string desc)[] props)
        {
            var p = new JObject();
            foreach (var (name, type, desc) in props) p[name] = new JObject { ["type"] = type, ["description"] = desc };
            return new JObject { ["type"] = "object", ["properties"] = p };
        }

        static List<ToolDef> BuiltIns() => new List<ToolDef>
        {
            new ToolDef
            {
                Name = "cur_task", BuiltIn = true, TimeoutSeconds = 300,
                Description = "Run a shared task of the claude using revit ribbon tab (same code as its buttons). task list_tasks lists tasks + args. Prefer this over re-deriving a job.",
                InputSchema = Require(Schema(("task", "string", "Task name, e.g. print_set_sync; list_tasks lists them"),
                                             ("args", "object", "Task args (see list_tasks); dryRun reports only")), "task"),
                OnApi = (app, a) => JToken.Parse(PyHost.RunTask(app, (string)a["task"],
                    (a["args"] ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None))),
            },
            new ToolDef
            {
                Name = "run_dynamic_command", BuiltIn = true, TimeoutSeconds = 300,
                Description = "Run a command of the commands folder by name (same as calling the tool <name>). source: run this C# text instead of the file.",
                InputSchema = Require(Schema(("name", "string", "Command (file name without .cs)"),
                                             ("args", "object", "Arguments of the command"),
                                             ("source", "string", "Optional C# source to run instead of the file")), "name"),
                OnApi = (app, a) => DynamicCommands.Run(app, (string)a["name"], (string)a["source"], a["args"] as JObject),
            },
            new ToolDef
            {
                Name = "cur_status", BuiltIn = true, ReadOnly = true,
                Description = "claude using revit state: this Revit window (PID, documents), MCP port holder / standby windows, clients, running job, last calls. Works while Revit is busy.",
                InputSchema = Schema(),
                OffApi = a => McpServer.Snapshot(true),
            },
        };

        static JObject Require(JObject schema, params string[] names)
        {
            schema["required"] = new JArray(names.Cast<object>().ToArray());
            return schema;
        }

        public static void Invalidate()
        {
            lock (Gate) { _tools = null; _stamp = null; }
        }

        static string Stamp()
        {
            string dir = Paths.Commands;
            if (!Directory.Exists(dir)) return "none";
            var files = Directory.GetFiles(dir, "*.cs");
            long sum = files.Sum(f => File.GetLastWriteTimeUtc(f).Ticks % 1000000007L);
            return files.Length + ":" + sum;
        }

        public static List<ToolDef> All()
        {
            lock (Gate)
            {
                string stamp = Stamp();
                if (_tools != null && stamp == _stamp) return _tools;
                var tools = BuiltIns();
                var taken = new HashSet<string>(tools.Select(t => t.Name));
                foreach (var info in DynamicCommands.Scan())
                {
                    if (!taken.Add(info.Name)) continue;
                    string name = info.Name;
                    tools.Add(new ToolDef
                    {
                        Name = name,
                        Description = info.HeaderError != null ? "(header error: " + info.HeaderError + ") " + info.Description : info.Description,
                        InputSchema = info.InputSchema,
                        ReadOnly = info.ReadOnly,
                        TimeoutSeconds = info.TimeoutSeconds,
                        OnApi = (app, a) => DynamicCommands.Run(app, name, null, a),
                    });
                }
                _tools = tools;
                _stamp = stamp;
                return tools;
            }
        }

        public static ToolDef Find(string name)
        {
            var t = All().FirstOrDefault(x => x.Name == name);
            if (t != null) return t;
            // a file added a moment ago (the watcher has not fired yet)
            if (name != null && DynamicCommands.NameRule.IsMatch(name) && File.Exists(Path.Combine(Paths.Commands, name + ".cs")))
            {
                Invalidate();
                return All().FirstOrDefault(x => x.Name == name);
            }
            return null;
        }

        /// <summary>Call a tool from any thread: API tools go through RevitQueue (or run directly on the API thread).</summary>
        public static object Call(ToolDef tool, JObject args)
        {
            args = args ?? new JObject();
            if (tool.OffApi != null) return tool.OffApi(args);
            if (RevitQueue.OnApiThread)
                return tool.OnApi(App.UIApp, args);
            return RevitQueue.Run(tool.Name, app => tool.OnApi(app, args),
                TimeSpan.FromSeconds(Math.Max(5, tool.TimeoutSeconds)));
        }

        /// <summary>For the web bridge (Python, already on the API thread): JSON in, JSON out.</summary>
        public static string CallJson(UIApplication app, string name, string argsJson)
        {
            var tool = Find(name);
            if (tool == null) return Json.Error("unknown tool: " + name);
            try
            {
                var args = string.IsNullOrEmpty(argsJson) ? new JObject() : JObject.Parse(argsJson);
                object result = tool.OffApi != null ? tool.OffApi(args) : tool.OnApi(app ?? App.UIApp, args);
                return ResultText(result);
            }
            catch (Exception ex) { return Json.Error(ex.Message); }
        }

        public static string ListJson()
            => new JArray(All().Select(t =>
            {
                var o = t.ToMcp();
                o["builtIn"] = t.BuiltIn;
                return o;
            })).ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>A command's return value as JSON text (a JToken of another Newtonsoft copy is re-read).</summary>
        public static string ResultText(object result)
        {
            if (result == null) return "null";
            if (result is string s) return s;
            var type = result.GetType();
            if (type.Namespace == "Newtonsoft.Json.Linq" && type.Assembly != typeof(JToken).Assembly)
                return JToken.Parse(result.ToString()).ToString(Newtonsoft.Json.Formatting.None);
            return Json.Serialize(result);
        }

        // ---- watch the commands folder ----------------------------------------------------

        public static void Watch()
        {
            try
            {
                string dir = Paths.Dir(Paths.Commands);
                _watcher = new FileSystemWatcher(dir, "*.cs") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                FileSystemEventHandler on = (s, e) => _debounce.Change(1500, Timeout.Infinite);
                _debounce = new Timer(_ => { Invalidate(); Changed?.Invoke(); }, null, Timeout.Infinite, Timeout.Infinite);
                _watcher.Created += on;
                _watcher.Deleted += on;
                _watcher.Changed += on;
                _watcher.Renamed += (s, e) => on(s, e);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("commands watcher: " + ex.Message); }
        }
    }
}
