using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PetFolio")]
[assembly: System.Reflection.AssemblyProduct("PetFolio")]
[assembly: System.Reflection.AssemblyDescription("PetFolio desktop companion for Codex Pet")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

// PetFolio reads quota and the pet anchor, never writes Codex settings.
static class Program {
    internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static void Record(string kind, object data) {
        lock (Json) File.AppendAllText(Path.Combine(Folder, "probe-events.jsonl"),
            Json.Serialize(new { at = DateTime.UtcNow.ToString("o"), kind = kind, data = data }) + "\n");
    }
    [STAThread] static void Main(string[] args) {
        using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\PetFolioStop")) {
            if (Array.IndexOf(args, "--stop") >= 0) { stop.Set(); return; }
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PetFolioShow")) {
            bool created;
            using (var mutex = new Mutex(true, "Local\\PetFolio", out created)) {
                if (!created) {show.Set();return;}
                stop.Reset();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try {
                    var configuration=StartupConfiguration.Resolve(args);
                    using (var host = new CompanionApplication(configuration.Executable, configuration.Home, stop, show)) Application.Run(host);
                }catch(Exception ex) {
                    MessageBox.Show(ex.Message,"PetFolio 啟動失敗",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    Environment.ExitCode=1;
                }
            }
            }
        }
    }
}

