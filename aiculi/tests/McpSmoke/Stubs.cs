// The few Revit / add-in types the MCP sources touch, so they run without Revit.
using System;
using System.Threading;

namespace Autodesk.Revit.DB
{
    public class Document { }
}

namespace Autodesk.Revit.UI
{
    public class UIApplication { }

    public interface IExternalEventHandler
    {
        void Execute(UIApplication app);
        string GetName();
    }

    public enum ExternalEventRequest { Accepted, Pending, Denied, TimedOut }

    /// <summary>One "API thread" that runs the handler each time the event is raised (like Revit's idle loop).</summary>
    public class ExternalEvent
    {
        readonly IExternalEventHandler _handler;
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        public static Thread ApiThread;

        ExternalEvent(IExternalEventHandler h)
        {
            _handler = h;
            ApiThread = new Thread(() =>
            {
                var app = new UIApplication();
                while (true) { _signal.WaitOne(); _handler.Execute(app); }
            }) { IsBackground = true, Name = "fake Revit API thread" };
            ApiThread.Start();
        }

        public static ExternalEvent Create(IExternalEventHandler h) => new ExternalEvent(h);
        public ExternalEventRequest Raise() { _signal.Set(); return ExternalEventRequest.Accepted; }
    }
}

namespace AiCuli
{
    public static class App
    {
        public static Autodesk.Revit.UI.UIApplication UIApp => null;
        public static string RevitVersion => "stub";
    }
}

namespace AiCuli.Python
{
    public static class PyHost
    {
        public static string RunTask(Autodesk.Revit.UI.UIApplication app, string task, string argsJson)
            => "{\"stubTask\":\"" + task + "\",\"args\":" + argsJson + "}";
    }
}
