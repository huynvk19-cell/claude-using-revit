using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Mcp;
using Newtonsoft.Json.Linq;

// McpSmoke selftest          the server in this process, checked over HTTP
// McpSmoke serve <seconds>   hold the port for a while (another process takes it over / the MCP SDK client calls it)
// McpSmoke takeover          start (expect standby), take the port over, report
// The data folder is %APPDATA%\claude-using-revit (on Linux: $XDG_CONFIG_HOME/claude-using-revit); smoke.sh points it at a temp folder.
static class Program
{
    static int _fail;

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "selftest";
        RevitQueue.Init();
        ToolRegistry.Watch();
        McpServer.Start();
        WaitState(s => s != ServerState.Off, 5);
        Console.WriteLine($"{mode}: state {McpServer.State} {McpServer.Note} (pid {Environment.ProcessId}, port {McpServer.Port})");
        switch (mode)
        {
            case "serve":
                Thread.Sleep(TimeSpan.FromSeconds(args.Length > 1 ? int.Parse(args[1]) : 60));
                Console.WriteLine($"serve: end state {McpServer.State} {McpServer.Note}");
                McpServer.Shutdown();
                return 0;
            case "takeover":
                Check("second window waits", McpServer.State == ServerState.Standby, McpServer.Note);
                bool ok = McpServer.TakeOver();
                Check("take over", ok && McpServer.State == ServerState.Listening, McpServer.State + " " + McpServer.Note);
                McpServer.Shutdown();
                return _fail == 0 ? 0 : 1;
            default:
                SelfTest().GetAwaiter().GetResult();
                McpServer.Shutdown();
                Console.WriteLine(_fail == 0 ? "ALL PASSED" : _fail + " FAILED");
                return _fail == 0 ? 0 : 1;
        }
    }

    static void WaitState(Func<ServerState, bool> ok, int seconds)
    {
        for (int i = 0; i < seconds * 10 && !ok(McpServer.State); i++) Thread.Sleep(100);
    }

    static void Check(string what, bool ok, string detail = null)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}{(ok || detail == null ? "" : ": " + detail)}");
        if (!ok) _fail++;
    }

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    static string Url => $"http://127.0.0.1:{McpServer.Port}/mcp";

    static async Task<(int code, JToken body)> Rpc(object message, string origin = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new StringContent(JToken.FromObject(message).ToString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Accept", "application/json, text/event-stream");
        if (origin != null) req.Headers.Add("Origin", origin);
        var resp = await Http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        return ((int)resp.StatusCode, text.Length > 0 ? JToken.Parse(text) : null);
    }

    static async Task<JObject> Call(string tool, object arguments, int id)
    {
        var (_, body) = await Rpc(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = tool, arguments } });
        return (JObject)body["result"];
    }

    static async Task SelfTest()
    {
        Check("listening", McpServer.State == ServerState.Listening, McpServer.Note);

        var (code, init) = await Rpc(new { jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "smoke", version = "1" } } });
        Check("initialize", code == 200 && (string)init["result"]["protocolVersion"] == "2025-06-18"
                            && (string)init["result"]["serverInfo"]["name"] == "claude-using-revit", init?.ToString());

        var (c2, b2) = await Rpc(new { jsonrpc = "2.0", method = "notifications/initialized" });
        Check("notification -> 202", c2 == 202 && b2 == null, c2.ToString());

        var (_, list) = await Rpc(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var names = ((JArray)list["result"]["tools"]).Select(t => (string)t["name"]).ToList();
        foreach (string n in new[] { "cur_task", "run_dynamic_command", "cur_status", "hello_test", "broken_test" })
            Check("tools/list has " + n, names.Contains(n), string.Join(",", names));
        var hello = ((JArray)list["result"]["tools"]).First(t => (string)t["name"] == "hello_test");
        Check("header readOnly -> annotations", (bool?)hello["annotations"]?["readOnlyHint"] == true, hello.ToString());

        var r1 = await Call("hello_test", new { n = 5 }, 3);
        string t1 = (string)r1["content"][0]["text"];
        Check("call hello_test", r1["isError"] == null && t1.Contains("\"n\":5") && t1.Contains("\"onApiThread\":true"), r1.ToString());

        var r2 = await Call("broken_test", new { }, 4);
        Check("compile error is a tool error", (bool?)r2["isError"] == true && ((string)r2["content"][0]["text"]).Contains("does not compile"), r2.ToString());

        var r3 = await Call("cur_task", new { task = "list_tasks", args = new { a = 1 } }, 5);
        Check("cur_task", ((string)r3["content"][0]["text"]).Contains("\"stubTask\":\"list_tasks\""), r3.ToString());

        var r4 = await Call("cur_status", new { }, 6);
        var st = JObject.Parse((string)r4["content"][0]["text"]);
        Check("cur_status", (string)st["state"] == "Listening" && st["lastCalls"] is JArray, st.ToString());

        var r5 = await Call("run_dynamic_command", new { name = "hello_test", args = new { n = 7 } }, 7);
        Check("run_dynamic_command", ((string)r5["content"][0]["text"]).Contains("\"n\":7"), r5.ToString());

        var r6 = await Call("no_such_tool", new { }, 8);
        Check("unknown tool", (bool?)r6["isError"] == true, r6.ToString());

        var (c7, b7) = await Rpc(new { jsonrpc = "2.0", id = 9, method = "tools/list" }, "https://evil.example");
        Check("foreign Origin refused", c7 == 403, c7.ToString());

        var (_, batch) = await Rpc(new object[] { new { jsonrpc = "2.0", id = 10, method = "ping" },
                                                 new { jsonrpc = "2.0", method = "notifications/initialized" },
                                                 new { jsonrpc = "2.0", id = 11, method = "nope" } });
        Check("batch", batch is JArray a && a.Count == 2 && (int)a[1]["error"]["code"] == -32601, batch?.ToString());

        // parallel calls run one at a time on the API thread, all answered
        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Call("hello_test", new { n = 100 + i }, 20 + i)));
        Check("6 parallel calls", parallel.All(p => p["isError"] == null), string.Join(" | ", parallel.Select(p => p.ToString())));

        // event stream: list_changed when a command file is added; edit -> recompiled on the next call
        var events = new List<string>();
        var cts = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Add("Accept", "text/event-stream");
            using (var resp = await new HttpClient { Timeout = Timeout.InfiniteTimeSpan }.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            using (var s = new StreamReader(await resp.Content.ReadAsStreamAsync()))
            {
                while (!cts.IsCancellationRequested)
                {
                    string line = await s.ReadLineAsync();
                    if (line == null) break;
                    lock (events) events.Add(line);
                }
            }
        });
        await Task.Delay(500);
        Check("stream counted as connected", McpServer.OpenStreams == 1 && McpServer.Connected, McpServer.OpenStreams.ToString());
        string dir = Paths.Commands;
        File.WriteAllText(Path.Combine(dir, "added_test.cs"), File.ReadAllText(Path.Combine(dir, "hello_test.cs")).Replace("class HelloTest", "class AddedTest"));
        string src = File.ReadAllText(Path.Combine(dir, "hello_test.cs"));
        File.WriteAllText(Path.Combine(dir, "hello_test.cs"), src.Replace("version = 1", "version = 2"));
        for (int i = 0; i < 50; i++) { lock (events) if (events.Any(e => e.Contains("list_changed"))) break; await Task.Delay(100); }
        lock (events) Check("tools/list_changed on the stream", events.Any(e => e.Contains("notifications/tools/list_changed")), string.Join("\\n", events));
        var r8 = await Call("hello_test", new { n = 1 }, 30);
        Check("edited command recompiled", ((string)r8["content"][0]["text"]).Contains("\"version\":2"), r8.ToString());
        var r9 = await Call("added_test", new { n = 2 }, 31);
        Check("added command callable", r9["isError"] == null, r9.ToString());
        cts.Cancel();

        var status = await Http.GetStringAsync($"http://127.0.0.1:{McpServer.Port}/cur/status");
        Check("/cur/status", JObject.Parse(status)["state"]?.ToString() == "Listening", status);
        var calls = CallLog.Recent(40);
        Check("call log", calls.Count >= 10, calls.Count.ToString());
    }
}
