using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

// Desktop identity is shared with PetStateService; CLI processes are never targets.
static class CodexDesktopSession {
    internal static bool Matches(string name,string path) {
        return String.Equals(name,"ChatGPT",StringComparison.OrdinalIgnoreCase)
            && path.IndexOf("OpenAI.Codex",StringComparison.OrdinalIgnoreCase)>=0;
    }
    internal static string Current() {
        var sessions=new System.Collections.Generic.List<string>();
        foreach(var p in Process.GetProcessesByName("ChatGPT"))using(p)try {
            if(p.SessionId==Process.GetCurrentProcess().SessionId && p.MainWindowHandle!=IntPtr.Zero && Matches(p.ProcessName,p.MainModule.FileName))
                sessions.Add(p.Id+":"+p.StartTime.ToUniversalTime().Ticks);
        }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}catch(NotSupportedException){}
        sessions.Sort(StringComparer.Ordinal);return String.Join(";",sessions.ToArray());
    }
}
sealed class AutoStartSettings {
    public string Executable,DesktopHome,CliHome,DataDirectory;
}
static class CodexAutoStart {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName="PetFolioCodexWatcher";
    internal static string SettingsPath(string data){return Path.Combine(data,"codex-autostart.json");}
    internal static AutoStartSettings Read(string data) {
        try {
            var value=new JavaScriptSerializer().Deserialize<AutoStartSettings>(File.ReadAllText(SettingsPath(data)));
            if(value==null || String.IsNullOrEmpty(value.Executable) || String.IsNullOrEmpty(value.DesktopHome)
                || String.IsNullOrEmpty(value.DataDirectory))return null;
            return value;
        }
        catch(IOException){return null;}catch(ArgumentException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    internal static bool Enabled(string data) {
        var settings=Read(data);if(settings==null)return false;
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null &&
            String.Equals(key.GetValue(RunName) as string,Quote(settings.Executable)+" --watch-codex "+Quote(data),StringComparison.OrdinalIgnoreCase);
    }
    internal static string Quote(string value){return "\""+value.TrimEnd('\\')+"\"";}
    internal static void Enable(string data,string home,string source) {
        Directory.CreateDirectory(data);
        string hash;using(var stream=File.OpenRead(source))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Substring(0,16);
        var directory=Path.Combine(data,"auto-start");Directory.CreateDirectory(directory);
        var binary=Path.Combine(directory,"PetFolio-"+hash+".exe");
        if(!File.Exists(binary))File.Copy(source,binary);
        var settings=new AutoStartSettings {Executable=binary,DesktopHome=home,
            CliHome=Environment.GetEnvironmentVariable("CODEX_HOME"),DataDirectory=data};
        File.WriteAllText(SettingsPath(data),new JavaScriptSerializer().Serialize(settings));
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey))key.SetValue(RunName,Quote(binary)+" --watch-codex "+Quote(data),RegistryValueKind.String);
        // Existing monitor reads updated settings. Start a detached WinExe watcher.
        Process.Start(new ProcessStartInfo(binary,"--watch-codex "+Quote(data)){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});
    }
    internal static void Disable(string data) {
        if(Enabled(data))using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))if(key!=null)key.DeleteValue(RunName,false);
        if(File.Exists(SettingsPath(data)))File.Delete(SettingsPath(data));
    }
    internal static void Suppress(string data) {
        if(!Enabled(data))return;
        try{File.WriteAllText(Path.Combine(data,"codex-autostart-suppressed.txt"),CodexDesktopSession.Current());}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    internal static bool ShouldLaunch(string session,string suppressed,bool running) {
        return !String.IsNullOrEmpty(session) && session!=suppressed && !running;
    }
    internal static void Watch(string data) {
        using(var mutex=new Mutex(false,"Local\\PetFolioCodexWatcher")) {
            bool owns;try{owns=mutex.WaitOne(5000);}catch(AbandonedMutexException){owns=true;}
            if(!owns)return;
            try {
                DateTime retry=DateTime.MinValue;
                while(Enabled(data)) {
                    var settings=Read(data);if(settings==null)return;
                    var session=CodexDesktopSession.Current();
                    var marker=Path.Combine(data,"codex-autostart-suppressed.txt");
                    if(session.Length==0) {if(File.Exists(marker))File.Delete(marker);retry=DateTime.MinValue;}
                    var suppressed=File.Exists(marker)?File.ReadAllText(marker):"";
                    if(ShouldLaunch(session,suppressed,CompanionSignals.Running()) && DateTime.UtcNow>=retry) {
                        retry=DateTime.UtcNow.AddSeconds(30);
                        try {
                            var start=new ProcessStartInfo(settings.Executable,"--background --data-directory "+Quote(settings.DesktopHome)) {
                                UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(settings.Executable)};
                            start.EnvironmentVariables["PETFOLIO_DATA_DIR"]=settings.DataDirectory;
                            if(!String.IsNullOrEmpty(settings.CliHome))start.EnvironmentVariables["CODEX_HOME"]=settings.CliHome;
                            using(var p=Process.Start(start)){}
                        }catch(System.ComponentModel.Win32Exception){}catch(IOException){}
                    }
                    Thread.Sleep(2000);
                }
            }finally{mutex.ReleaseMutex();}
        }
    }
}
