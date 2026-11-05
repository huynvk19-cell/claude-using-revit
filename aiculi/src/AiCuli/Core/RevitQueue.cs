using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;

namespace AiCuli.Core
{
    /// <summary>
    /// Runs work on Revit's API thread, one job at a time, through one ExternalEvent.
    /// A caller on another thread (the MCP server) waits for its job. A job that has not STARTED within
    /// startTimeout is dropped (Revit busy, a modal dialog open); a job that started always runs to the end.
    /// </summary>
    public static class RevitQueue
    {
        public class Job
        {
            public string Label;
            public Func<UIApplication, object> Work;
            public object Result;
            public Exception Error;
            public DateTime? StartedUtc;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        class Handler : IExternalEventHandler
        {
            public void Execute(UIApplication app) => RunNext(app);
            public string GetName() => "AI Culi";
        }

        static readonly object Gate = new object();
        static readonly LinkedList<Job> Queue = new LinkedList<Job>();
        static ExternalEvent _event;
        static int _apiThread;

        public static Job Current { get; private set; }
        public static int Waiting { get { lock (Gate) return Queue.Count; } }

        /// <summary>Call once in API context (OnStartup).</summary>
        public static void Init()
        {
            if (_event == null) _event = ExternalEvent.Create(new Handler());
            _apiThread = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool OnApiThread => Thread.CurrentThread.ManagedThreadId == _apiThread;

        /// <summary>Run work on the API thread and return its result (throws its exception).</summary>
        public static object Run(string label, Func<UIApplication, object> work, TimeSpan startTimeout)
        {
            if (_event == null) throw new InvalidOperationException("AI Culi is not started yet");
            var job = new Job { Label = label, Work = work };
            lock (Gate) Queue.AddLast(job);
            var raised = _event.Raise();
            if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
                Log.Write($"queue: ExternalEvent.Raise -> {raised}");
            if (!job.Done.Wait(startTimeout))
            {
                lock (Gate)
                {
                    if (job.StartedUtc == null && Queue.Remove(job))
                        throw new TimeoutException(
                            $"Revit is busy: '{label}' did not start within {(int)startTimeout.TotalSeconds} s " +
                            "(a dialog or another command is open in Revit). Nothing was run.");
                }
                job.Done.Wait();
            }
            if (job.Error != null) throw job.Error;
            return job.Result;
        }

        static void RunNext(UIApplication app)
        {
            Job job;
            lock (Gate)
            {
                if (Queue.Count == 0) return;
                job = Queue.First.Value;
                Queue.RemoveFirst();
                job.StartedUtc = DateTime.UtcNow;
                Current = job;
            }
            try { job.Result = job.Work(app); }
            catch (Exception ex) { job.Error = ex; }
            finally
            {
                bool more;
                lock (Gate)
                {
                    Current = null;
                    more = Queue.Count > 0;
                }
                job.Done.Set();
                if (more) _event.Raise();
            }
        }
    }
}
