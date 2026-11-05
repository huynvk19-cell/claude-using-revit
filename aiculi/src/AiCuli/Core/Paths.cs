using System;
using System.IO;
using System.Reflection;

namespace AiCuli.Core
{
    /// <summary>
    /// Where AI Culi keeps things. Everything lives under %APPDATA%\AiCuli unless config.json says otherwise:
    ///   bin\&lt;revit&gt;\   this add-in (AiCuli.dll, IronPython, Lib\ = Python standard library, roslyn\)
    ///   extension\      the ribbon tools + shared tasks (Python, from aiculi/extension of the repo)
    ///   commands\       the MCP commands (C#, from revit-commands/ of the repo)
    ///   config.json     settings (Config.cs)   logs\  open-docs\  instances\  web-bridge\  tool-config\
    /// </summary>
    public static class Paths
    {
        public static readonly string Data =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiCuli");

        public static string AddinDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string StdLib => Path.Combine(AddinDir, "Lib");
        public static string RoslynDir => Path.Combine(AddinDir, "roslyn");

        public static string Extension => Pick(Config.Current.ExtensionDir, Path.Combine(Data, "extension"));
        public static string ExtensionLib => Path.Combine(Extension, "lib");
        public static string Commands => Pick(Config.Current.CommandsDir, Path.Combine(Data, "commands"));

        public static string Logs => Dir(Path.Combine(Data, "logs"));
        public static string Instances => Dir(Path.Combine(Data, "instances"));
        public static string ToolConfig => Dir(Path.Combine(Data, "tool-config"));
        public static string ConfigFile => Path.Combine(Data, "config.json");
        public static string ControlKeyFile => Path.Combine(Data, "control.key");

        static string Pick(string configured, string fallback)
        {
            if (string.IsNullOrWhiteSpace(configured)) return fallback;
            return Environment.ExpandEnvironmentVariables(configured);
        }

        public static string Dir(string path)
        {
            try { Directory.CreateDirectory(path); } catch { /* reported where it is used */ }
            return path;
        }
    }
}
