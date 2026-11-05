using System;
using System.IO;
using Newtonsoft.Json;

namespace AiCuli.Core
{
    /// <summary>
    /// %APPDATA%\AiCuli\config.json. Written with the defaults on first start; edit it and restart Revit.
    /// extensionDir / commandsDir may point at a repo checkout (install.ps1 -Dev): edits there apply at once.
    /// </summary>
    public class Config
    {
        [JsonProperty("port")] public int Port { get; set; } = 8970;
        [JsonProperty("autoStart")] public bool AutoStart { get; set; } = true;
        [JsonProperty("extensionDir")] public string ExtensionDir { get; set; }
        [JsonProperty("commandsDir")] public string CommandsDir { get; set; }
        [JsonProperty("tabName")] public string TabName { get; set; }

        static Config _current;
        public static Config Current => _current ?? (_current = Load());

        static Config Load()
        {
            var cfg = new Config();
            try
            {
                if (File.Exists(Paths.ConfigFile))
                    cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Paths.ConfigFile)) ?? new Config();
                else
                {
                    Directory.CreateDirectory(Paths.Data);
                    File.WriteAllText(Paths.ConfigFile, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                }
            }
            catch (Exception ex) { Log.Write("config: " + ex.Message + " (defaults used)"); }
            if (cfg.Port <= 0 || cfg.Port > 65535) cfg.Port = 8970;
            return cfg;
        }
    }
}
