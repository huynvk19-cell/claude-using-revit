using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AiCuli.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiCuli.Mcp
{
    public enum ServerState { Off, Listening, Standby, Error }

    /// <summary>
    /// The MCP server inside Revit (Streamable HTTP, JSON-RPC 2.0) at http://127.0.0.1:&lt;port&gt;/mcp.
    ///   claude mcp add --transport http --scope user aiculi http://127.0.0.1:8970/mcp
    /// One port for every Revit window: the first window that starts holds it ("Listening"), the others wait
    /// ("Standby") and take it within a few seconds when it is free (that window closed or let go). A window
    /// can take it over: the ribbon status window, culi_status, or tools/revit-port take -RevitPid N.
    /// Also: GET /mcp = event stream (an open stream = a connected client, and tools/list_changed goes there),
    /// GET /aiculi/status, POST /aiculi/release (needs the key in %APPDATA%\AiCuli\control.key).
    /// </summary>
    public static class McpServer
    {
        public class Client
        {
            public string Name;
            public string Version;
            public string Remote;
            public DateTime FirstUtc;
            public DateTime LastUtc;
        }

        static readonly string[] Protocols = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
        static readonly object Gate = new object();
        static readonly ConcurrentDictionary<string, Client> Clients = new ConcurrentDictionary<string, Client>();
        static readonly ConcurrentDictionary<HttpConnection, BlockingCollection<string>> Streams =
            new ConcurrentDictionary<HttpConnection, BlockingCollection<string>>();

        static MiniHttpServer _http;
        static Timer _timer;
        static DateTime _yieldUntilUtc;
        static DateTime _lastRequestUtc;
        static string _lastClient;
        static int _pid = Process.GetCurrentProcess().Id;

        public static ServerState State { get; private set; } = ServerState.Off;
        public static string Note { get; private set; } = "";
        public static bool Enabled { get; private set; }
        public static int Port => Config.Current.Port;
        public static int OpenStreams => Streams.Count;
        public static DateTime LastRequestUtc => _lastRequestUtc;
        public static string LastClient => _lastClient;
        public static string Version => typeof(McpServer).Assembly.GetName().Version.ToString(3);

        /// <summary>A client counts as connected while it keeps its event stream open, or for 3 min after a request.</summary>
        public static bool Connected =>
            State == ServerState.Listening && (Streams.Count > 0 || DateTime.UtcNow - _lastRequestUtc < TimeSpan.FromMinutes(3));

        // ---- start / stop / hand over -------------------------------------------------------

        public static void Start()
        {
            Enabled = true;
            _yieldUntilUtc = DateTime.MinValue;
            if (_timer == null)
            {
                _timer = new Timer(_ => Tick(), null, 0, 3000);
                ToolRegistry.Changed += () => Broadcast("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}");
            }
            else Tick();
        }

        public static void Stop(string note = "đã tắt")
        {
            Enabled = false;
            CloseListener();
            State = ServerState.Off;
            Note = note;
            WriteInstance();
            Log.Write("mcp: stopped (" + note + ")");
        }

        public static void Shutdown()
        {
            try { _timer?.Dispose(); } catch { }
            CloseListener();
            try { File.Delete(InstanceFile(_pid)); } catch { }
        }

        static void CloseListener()
        {
            lock (Gate)
            {
                var h = _http;
                _http = null;
                h?.Stop();
                foreach (var s in Streams.Values) s.CompleteAdding();
            }
        }

        static void Tick()
        {
            try
            {
                if (File.Exists(TakeFile(_pid)))
                {
                    try { File.Delete(TakeFile(_pid)); } catch { }
                    Log.Write("mcp: take-over asked (take file)");
                    TakeOver();
                }
                else if (Enabled && _http == null && DateTime.UtcNow >= _yieldUntilUtc) TryBind();
                WriteInstance();
            }
            catch (Exception ex) { Log.Error("mcp tick", ex); }
        }

        static bool TryBind()
        {
            lock (Gate)
            {
                if (_http != null) return true;
                var server = new MiniHttpServer(Handle);
                if (server.Start(Port, out string error))
                {
                    _http = server;
                    State = ServerState.Listening;
                    Note = "";
                    Log.Write($"mcp: listening on 127.0.0.1:{Port}");
                    return true;
                }
                if (error == "in use")
                {
                    State = ServerState.Standby;
                    var holder = OtherInstances().FirstOrDefault(i => (string)i["role"] == "holder");
                    Note = holder != null
                        ? $"cửa sổ Revit PID {holder["pid"]} đang giữ cổng {Port}"
                        : $"cổng {Port} đang bị một chương trình khác dùng";
                }
                else
                {
                    State = ServerState.Error;
                    Note = error;
                }
                return false;
            }
        }

        /// <summary>Ask the window holding the port to let go, then take it (a few tries). True when this window holds it.</summary>
        public static bool TakeOver()
        {
            Enabled = true;
            _yieldUntilUtc = DateTime.MinValue;
            if (_http != null) return true;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/aiculi/release");
                    req.Headers.Add("X-AiCuli-Key", ControlKey());
                    req.Headers.Add("X-AiCuli-Pid", _pid.ToString());
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    var resp = http.SendAsync(req).GetAwaiter().GetResult();
                    Log.Write($"mcp: release asked -> {(int)resp.StatusCode}");
                }
            }
            catch (Exception ex) { Log.Write("mcp: release request: " + ex.Message); }
            for (int i = 0; i < 20; i++)
            {
                if (TryBind()) { WriteInstance(); return true; }
                Thread.Sleep(250);
            }
            WriteInstance();
            return false;
        }

        static void YieldTo(string who)
        {
            CloseListener();
            _yieldUntilUtc = DateTime.UtcNow.AddSeconds(30);
            State = ServerState.Standby;
            Note = "đã nhường cổng cho " + who;
            WriteInstance();
            Log.Write("mcp: " + Note);
        }

        // ---- instances (one file per Revit window) ------------------------------------------

        static string InstanceFile(int pid) => Path.Combine(Paths.Instances, pid + ".json");
        static string TakeFile(int pid) => Path.Combine(Paths.Instances, pid + ".take");

        static string _lastWritten;
        static DateTime _lastWriteUtc;

        static void WriteInstance()
        {
            try
            {
                var p = Process.GetCurrentProcess();
                var o = new JObject
                {
                    ["pid"] = _pid,
                    ["role"] = State == ServerState.Listening ? "holder" : State == ServerState.Standby ? "standby" : "off",
                    ["port"] = Port,
                    ["note"] = Note,
                    ["revit"] = App.RevitVersion,
                    ["window"] = SafeTitle(p),
                    ["version"] = Version,
                    ["connected"] = Connected,
                    ["client"] = _lastClient,
                };
                string text = o.ToString(Formatting.None);
                if (text == _lastWritten && DateTime.UtcNow - _lastWriteUtc < TimeSpan.FromSeconds(30)) return;
                o["updatedUtc"] = DateTime.UtcNow.ToString("o");
                string file = InstanceFile(_pid);
                File.WriteAllText(file + ".tmp", o.ToString(Formatting.Indented));
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
                _lastWritten = text;
                _lastWriteUtc = DateTime.UtcNow;
            }
            catch { /* next tick */ }
        }

        static string SafeTitle(Process p)
        {
            try { return p.MainWindowTitle; } catch { return ""; }
        }

        /// <summary>The other live Revit windows running AI Culi (files of closed windows are removed).</summary>
        public static List<JObject> OtherInstances()
        {
            var list = new List<JObject>();
            try
            {
                foreach (string f in Directory.GetFiles(Paths.Instances, "*.json"))
                {
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(f), out int pid) || pid == _pid) continue;
                    if (!Alive(pid)) { try { File.Delete(f); } catch { } continue; }
                    try { list.Add(JObject.Parse(File.ReadAllText(f))); } catch { }
                }
            }
            catch { }
            return list;
        }

        static bool Alive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; } catch { return false; }
        }

        public static string ControlKey()
        {
            string f = Paths.ControlKeyFile;
            try
            {
                if (File.Exists(f)) { string k = File.ReadAllText(f).Trim(); if (k.Length >= 16) return k; }
                var bytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                string key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(f, key);
                return key;
            }
            catch { return "aiculi-no-key-file"; }
        }

        // ---- status --------------------------------------------------------------------------

        public static object Snapshot(bool detail)
        {
            var job = RevitQueue.Current;
            var o = new JObject
            {
                ["pid"] = _pid,
                ["revit"] = App.RevitVersion,
                ["version"] = Version,
                ["state"] = State.ToString(),
                ["note"] = Note,
                ["enabled"] = Enabled,
                ["url"] = $"http://127.0.0.1:{Port}/mcp",
                ["connected"] = Connected,
                ["openStreams"] = Streams.Count,
                ["lastClient"] = _lastClient,
                ["lastRequestUtc"] = _lastRequestUtc == default(DateTime) ? null : _lastRequestUtc.ToString("o"),
                ["running"] = job == null ? null : new JObject
                {
                    ["tool"] = job.Label,
                    ["seconds"] = job.StartedUtc.HasValue ? (int)(DateTime.UtcNow - job.StartedUtc.Value).TotalSeconds : 0,
                },
                ["queued"] = RevitQueue.Waiting,
                ["clients"] = new JArray(Clients.Values.OrderByDescending(c => c.LastUtc).Take(5).Select(c => new JObject
                {
                    ["name"] = c.Name, ["version"] = c.Version, ["lastUtc"] = c.LastUtc.ToString("o"),
                })),
                ["otherWindows"] = new JArray(OtherInstances().Select(i => new JObject
                {
                    ["pid"] = i["pid"], ["role"] = i["role"], ["revit"] = i["revit"], ["window"] = i["window"],
                })),
            };
            if (detail)
            {
                o["openDocs"] = ReadOpenDocs();
                o["lastCalls"] = new JArray(CallLog.Recent(10).Select(c => new JObject
                {
                    ["at"] = c.At.ToString("HH:mm:ss"), ["tool"] = c.Tool, ["ms"] = c.Ms, ["ok"] = c.Ok, ["note"] = c.Note,
                }));
                o["commandsDir"] = Paths.Commands;
                o["extensionDir"] = Paths.Extension;
                o["log"] = Log.File;
            }
            return o;
        }

        static JToken ReadOpenDocs()
        {
            try
            {
                string f = Path.Combine(Paths.Data, "open-docs", _pid + ".json");
                return File.Exists(f) ? JToken.Parse(File.ReadAllText(f))["docs"] : null;
            }
            catch { return null; }
        }

        // ---- HTTP ----------------------------------------------------------------------------

        static void Handle(HttpRequest req, HttpConnection conn)
        {
            string path = req.Path.Split('?')[0].TrimEnd('/');
            if (path.Length == 0) path = "/";
            // a web page must never drive Revit (DNS rebinding): browsers send Origin, MCP clients do not
            string origin = req.Header("Origin");
            if (!string.IsNullOrEmpty(origin) && origin != "null"
                && !origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
            {
                conn.SendJson(403, Json.Error("forbidden origin"));
                return;
            }
            if (path == "/mcp" || path == "/")
            {
                if (req.Method == "POST") { HandleRpc(req, conn); return; }
                if (req.Method == "GET" && (req.Header("Accept") ?? "").Contains("text/event-stream")) { HandleStream(conn); return; }
                if (req.Method == "DELETE") { conn.Send(200, null, ""); return; }
                conn.Send(405, "text/plain", "POST JSON-RPC to /mcp", new Dictionary<string, string> { ["Allow"] = "GET, POST, DELETE" });
                return;
            }
            if (path == "/aiculi/status" && req.Method == "GET")
            {
                conn.SendJson(200, Json.Serialize(Snapshot(false)));
                return;
            }
            if (path == "/aiculi/release" && req.Method == "POST")
            {
                if (req.Header("X-AiCuli-Key") != ControlKey()) { conn.SendJson(403, Json.Error("bad key")); return; }
                string who = "PID " + (req.Header("X-AiCuli-Pid") ?? "?");
                conn.CloseAfterResponse = true;
                conn.SendJson(200, "{\"released\":true}");
                ThreadPool.QueueUserWorkItem(_ => YieldTo(who));
                return;
            }
            conn.SendJson(404, Json.Error("not found"));
        }

        static void HandleStream(HttpConnection conn)
        {
            var queue = new BlockingCollection<string>();
            Streams[conn] = queue;
            Log.Write("mcp: event stream opened " + conn.Remote);
            try
            {
                conn.StartEventStream();
                while (!conn.Closed)
                {
                    if (queue.TryTake(out string msg, 15000)) conn.WriteEvent(msg);
                    else if (queue.IsAddingCompleted) break;
                    else conn.WriteComment("ping");
                }
            }
            catch { /* client went away */ }
            finally
            {
                Streams.TryRemove(conn, out _);
                Log.Write("mcp: event stream closed " + conn.Remote);
            }
        }

        static void Broadcast(string message)
        {
            foreach (var q in Streams.Values)
            {
                try { q.Add(message); } catch { }
            }
        }

        static void HandleRpc(HttpRequest req, HttpConnection conn)
        {
            _lastRequestUtc = DateTime.UtcNow;
            JToken body;
            try { body = JToken.Parse(req.BodyText); }
            catch (Exception ex)
            {
                conn.SendJson(400, RpcError(null, -32700, "parse error: " + ex.Message).ToString(Formatting.None));
                return;
            }
            var messages = body is JArray arr ? arr.OfType<JObject>().ToList() : new List<JObject> { body as JObject };
            var replies = new JArray();
            foreach (var m in messages.Where(x => x != null))
            {
                var reply = HandleMessage(m, req);
                if (reply != null) replies.Add(reply);
            }
            if (replies.Count == 0) { conn.Send(202, null, ""); return; }
            conn.SendJson(200, (body is JArray ? (JToken)replies : replies[0]).ToString(Formatting.None));
        }

        static JObject HandleMessage(JObject m, HttpRequest req)
        {
            string method = (string)m["method"];
            JToken id = m["id"];
            if (method == null) return null;                  // a response from the client: nothing to do
            bool notification = id == null || id.Type == JTokenType.Null;
            try
            {
                switch (method)
                {
                    case "initialize":
                        return Reply(id, Initialize(m["params"] as JObject ?? new JObject(), req));
                    case "ping":
                        return notification ? null : Reply(id, new JObject());
                    case "tools/list":
                        return Reply(id, new JObject { ["tools"] = new JArray(ToolRegistry.All().Select(t => t.ToMcp())) });
                    case "tools/call":
                        return Reply(id, CallTool(m["params"] as JObject ?? new JObject()));
                    case "resources/list":
                        return Reply(id, new JObject { ["resources"] = new JArray() });
                    case "prompts/list":
                        return Reply(id, new JObject { ["prompts"] = new JArray() });
                    default:
                        if (notification) return null;        // notifications/initialized, cancelled, ...
                        return RpcError(id, -32601, "method not found: " + method);
                }
            }
            catch (Exception ex)
            {
                Log.Error("mcp " + method, ex);
                return notification ? null : RpcError(id, -32603, ex.Message);
            }
        }

        static JObject Initialize(JObject p, HttpRequest req)
        {
            string asked = (string)p["protocolVersion"];
            string version = Protocols.Contains(asked) ? asked : Protocols[1];
            var info = p["clientInfo"] as JObject;
            string name = (string)info?["name"] ?? (req.Header("User-Agent") ?? "client");
            var c = Clients.GetOrAdd(name, n => new Client { Name = n, FirstUtc = DateTime.UtcNow });
            c.Version = (string)info?["version"];
            c.Remote = req.Remote;
            c.LastUtc = DateTime.UtcNow;
            _lastClient = name;
            Log.Write($"mcp: initialize from {name} {c.Version} ({version})");
            return new JObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = true } },
                ["serverInfo"] = new JObject { ["name"] = "aiculi", ["title"] = "AI Culi (Revit)", ["version"] = Version },
                ["instructions"] =
                    "AI Culi: Revit tools of the window holding the port (culi_status says which, and its documents). " +
                    "One tool per command file (C#, compiled on first call, re-compiled when edited); culi_task runs the " +
                    "shared tasks of the AI Culi ribbon (task list_tasks lists them). Calls run one at a time on Revit's API thread.",
            };
        }

        static JObject CallTool(JObject p)
        {
            string name = (string)p["name"];
            var args = p["arguments"] as JObject ?? new JObject();
            var tool = ToolRegistry.Find(name);
            if (tool == null) return ToolResult("unknown tool: " + name + " (culi_status shows the commands folder)", true);
            if (_lastClient != null && Clients.TryGetValue(_lastClient, out var c)) c.LastUtc = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();
            try
            {
                string text = ToolRegistry.ResultText(ToolRegistry.Call(tool, args));
                bool isError = LooksLikeError(text);
                CallLog.Add(new CallRecord { At = DateTime.Now, Tool = name, Client = _lastClient, Ms = sw.ElapsedMilliseconds, Ok = !isError,
                                             Note = isError ? Short(text) : null });
                return ToolResult(text, false);
            }
            catch (Exception ex)
            {
                string msg = ex is CommandCompileException || ex is TimeoutException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
                CallLog.Add(new CallRecord { At = DateTime.Now, Tool = name, Client = _lastClient, Ms = sw.ElapsedMilliseconds, Ok = false, Note = Short(msg) });
                if (!(ex is CommandCompileException) && !(ex is TimeoutException)) Log.Error("tool " + name, ex);
                return ToolResult(msg, true);
            }
        }

        static bool LooksLikeError(string text)
            => text.StartsWith("{\"error\"", StringComparison.Ordinal);

        static string Short(string s) => s == null ? null : (s.Length > 160 ? s.Substring(0, 160) + "…" : s);

        static JObject ToolResult(string text, bool isError)
        {
            var o = new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }) };
            if (isError) o["isError"] = true;
            return o;
        }

        static JObject Reply(JToken id, JToken result) => new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

        static JObject RpcError(JToken id, int code, string message) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = code, ["message"] = message } };
    }
}
