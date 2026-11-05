using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AiCuli.Core
{
    /// <summary>%APPDATA%\AiCuli\logs\aiculi-yyyy-MM-dd.log, one line per event, PID first (several Revit windows).</summary>
    public static class Log
    {
        static readonly object Gate = new object();
        static readonly int Pid = Process.GetCurrentProcess().Id;

        public static string File => Path.Combine(Paths.Logs, "aiculi-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                    System.IO.File.AppendAllText(File, $"{DateTime.Now:HH:mm:ss.fff} [{Pid}] {message}{Environment.NewLine}");
            }
            catch { /* logging never breaks Revit */ }
        }

        public static void Error(string where, Exception ex) => Write($"{where}: {ex}");
    }

    /// <summary>The last tool calls, for the status window and culi_status.</summary>
    public class CallRecord
    {
        public DateTime At { get; set; }
        public string Tool { get; set; }
        public string Client { get; set; }
        public long Ms { get; set; }
        public bool Ok { get; set; }
        public string Note { get; set; }
    }

    public static class CallLog
    {
        static readonly LinkedList<CallRecord> Items = new LinkedList<CallRecord>();
        const int Max = 40;

        public static void Add(CallRecord r)
        {
            lock (Items)
            {
                Items.AddFirst(r);
                while (Items.Count > Max) Items.RemoveLast();
            }
            Log.Write($"call {r.Tool} {(r.Ok ? "ok" : "ERROR")} {r.Ms} ms {r.Client} {r.Note}");
        }

        public static List<CallRecord> Recent(int n)
        {
            lock (Items)
            {
                var list = new List<CallRecord>();
                foreach (var r in Items) { if (list.Count >= n) break; list.Add(r); }
                return list;
            }
        }
    }
}
