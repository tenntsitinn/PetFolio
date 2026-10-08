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
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

// PetFolio reads quota and the pet anchor, never writes Codex settings.
static class Program {
    internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
    internal static string DataFolder { get { return RuntimeData.Resolve(); } }
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static void Record(string kind, object data) {
        Directory.CreateDirectory(DataFolder);
        lock (Json) File.AppendAllText(Path.Combine(DataFolder, "probe-events.jsonl"),
            Json.Serialize(new { at = DateTime.UtcNow.ToString("o"), kind = kind, data = data }) + "\n");
    }
    [STAThread] static void Main(string[] args) {
        // WinExe may have redirected pipes but no console handle. Setting
        // OutputEncoding calls SetConsoleOutputCP and fails in that case.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});
        if(args.Length>0 && (args[0]=="--status" || args[0]=="--check")) {
            try {
                if(args[0]=="--status") {
                    if(args.Length!=1)throw new ArgumentException("--status 不接受路徑參數。");
                    Console.WriteLine(Json.Serialize(new {ok=true,running=CompanionSignals.Running(),
                        ready=CompanionSignals.Ready(),dataDirectory=DataFolder,
                        version=typeof(Program).Assembly.GetName().Version.ToString(3)}));
                }else {
                    var paths=new string[args.Length-1];Array.Copy(args,1,paths,0,paths.Length);
                    var configuration=StartupConfiguration.Resolve(paths);
                    Console.WriteLine(Json.Serialize(new {ok=true,codexExecutable=configuration.Executable,
                        codexDataDirectory=configuration.Home,dataDirectory=DataFolder,
                        authentication="not-checked",petVisibility="not-checked"}));
                }
            }catch(Exception ex) {
                Console.WriteLine(Json.Serialize(new {ok=false,error=ex.Message}));Environment.ExitCode=1;
            }
            return;
        }
        if(args.Length==1 && args[0]=="--stop") {CompanionSignals.Stop();return;}
        bool background=Array.IndexOf(args,"--background")>=0;
        if(background)args=new List<string>(args).FindAll(arg=>arg!="--background").ToArray();
        string startupResult=null;
        using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, CompanionSignals.Prefix+"Stop"))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, CompanionSignals.Prefix+"Show"))
        using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, CompanionSignals.Prefix+"Ready"))
        using (var mutex = new Mutex(false, CompanionSignals.Prefix)) {
                bool owns;
                try {owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                if (!owns) {show.Set();return;}
                stop.Reset();ready.Reset();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try {
                    var launchArguments=new List<string>(args);
                    var resultIndex=launchArguments.IndexOf("--startup-result");
                    if(resultIndex>=0) {
                        if(resultIndex+1>=launchArguments.Count)throw new ArgumentException("--startup-result requires an absolute file path.");
                        var resultPath=launchArguments[resultIndex+1];
                        var pathRoot=Path.GetPathRoot(resultPath);
                        if(!Path.IsPathRooted(resultPath) || pathRoot.Length<=1 || pathRoot.EndsWith(":"))
                            throw new ArgumentException("--startup-result requires an absolute file path.");
                        startupResult=Path.GetFullPath(resultPath);launchArguments.RemoveRange(resultIndex,2);
                    }
                    args=launchArguments.ToArray();
                    var configuration=StartupConfiguration.Resolve(args);
                    RuntimeData.Initialize(DataFolder,Folder);
                    using (var host = new CompanionApplication(configuration.Executable, configuration.Home, stop, show)) {
                        if(startupResult!=null)File.WriteAllText(startupResult,Json.Serialize(new {ok=true}),new UTF8Encoding(false));
                        startupResult=null;ready.Set();Application.Run(host);
                    }
                }catch(Exception ex) {
                    if(background) {
                        var failure=Json.Serialize(new {ok=false,error=ex.Message});
                        if(startupResult!=null)try{File.WriteAllText(startupResult,failure,new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}
                        Console.WriteLine(failure);
                    }
                    else MessageBox.Show(ex.Message,"PetFolio 啟動失敗",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    Environment.ExitCode=1;
                }finally {ready.Reset();mutex.ReleaseMutex();}
        }
    }
}

