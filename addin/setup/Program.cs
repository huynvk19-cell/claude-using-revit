using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ClaudeUsingRevit.Setup
{
    /// <summary>
    /// ClaudeUsingRevit-Setup.exe: everything in one file (the payload zip is a resource of this exe).
    ///   double-click          the install window
    ///   /uninstall            the window in uninstall mode (Windows Settings > Apps runs this)
    /// Built by addin/setup/make-setup.sh (needs only the .NET SDK; runs on Linux CI too).
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = Array.Exists(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            var asm = Assembly.GetExecutingAssembly();
            Func<Stream> payload = () => asm.GetManifestResourceStream("payload.zip")
                                         ?? throw new InvalidOperationException("Bộ cài bị hỏng (thiếu payload.zip). Hãy tải lại.");
            string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = version.IndexOf('+');
            if (plus > 0) version = version.Substring(0, plus);
            try { Application.Run(new MainForm(payload, version, uninstall)); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "claude using revit - Cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
