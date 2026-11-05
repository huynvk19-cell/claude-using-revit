using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ClaudeUsingRevit.Setup
{
    /// <summary>The entry in Windows Settings > Apps (Gỡ cài đặt there runs this exe with /uninstall).</summary>
    public static class WindowsBits
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeUsingRevit";

        /// <summary>Copy this exe next to the data (so uninstalling works after the download is gone) and register it.</summary>
        public static void Register(InstallerCore core, string version)
        {
            string self = Assembly.GetEntryAssembly().Location;
            string kept = Path.Combine(core.Data, "Setup.exe");
            try
            {
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, kept, true);
            }
            catch (Exception ex) { core.Log("Không chép được Setup.exe: " + ex.Message); return; }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    k.SetValue("DisplayName", "claude using revit");
                    k.SetValue("DisplayVersion", version ?? "");
                    k.SetValue("Publisher", "claude using revit");
                    k.SetValue("DisplayIcon", kept);
                    k.SetValue("InstallLocation", core.Data);
                    k.SetValue("UninstallString", "\"" + kept + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(kept).Length / 1024 * 3), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { core.Log("Không ghi được mục Gỡ cài đặt của Windows: " + ex.Message); }
        }

        public static void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Key, false); } catch { }
        }
    }
}
