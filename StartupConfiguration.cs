using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Startup discovery is shared by direct EXE launches and script launchers.
sealed class StartupConfiguration {
    public string Executable { get; private set; }
    public string Home { get; private set; }
    public static StartupConfiguration Resolve(string[] args) {
        if(args.Length!=0 && args.Length!=2)
            throw new ArgumentException("用法：PetFolio.exe [Codex CLI 路徑] [Codex 資料目錄]");
        var executables=new List<string>();
        if(args.Length==2) executables.Add(args[0]);
        else {
            foreach(var process in Process.GetProcessesByName("codex")) {
                using(process) try {
                    var path=process.MainModule.FileName;
                    if(path.IndexOf("OpenAI\\Codex\\bin\\",StringComparison.OrdinalIgnoreCase)>=0) executables.Add(path);
                }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
            }
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if(Directory.Exists(root)) {
                var files=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories);
                Array.Sort(files,(a,b)=>File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                executables.AddRange(files);
            }
            foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
                if(!String.IsNullOrWhiteSpace(directory)) try {executables.Add(Path.Combine(directory.Trim('"'),"codex.exe"));}catch(ArgumentException){}
            }
        }
        var homes=args.Length==2 ? new[]{args[1]} : new[]{Environment.GetEnvironmentVariable("CODEX_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex")};
        return FromCandidates(executables,homes);
    }
    internal static StartupConfiguration FromCandidates(IEnumerable<string> executables,IEnumerable<string> homes) {
        string executable=null,home=null;
        foreach(var candidate in executables) if(!String.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) {executable=Path.GetFullPath(candidate);break;}
        if(executable==null) throw new InvalidOperationException("找不到 Codex CLI。請先安裝並開啟 Codex；自訂安裝可用 PetFolio.exe 指定 CLI 與資料目錄，詳見 README。 ");
        foreach(var candidate in homes) if(!String.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate,".codex-global-state.json"))) {home=Path.GetFullPath(candidate);break;}
        if(home==null) throw new InvalidOperationException("找不到 Codex 桌面資料。請先開啟 Codex 並啟用 Pet；自訂資料目錄請設定 CODEX_HOME，或以命令列指定，詳見 README。");
        return new StartupConfiguration {Executable=executable,Home=home};
    }
}
