using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Startup discovery is shared by direct EXE launches and script launchers.
sealed class StartupConfiguration {
    public string Executable { get; private set; }
    public string Home { get; private set; }
    public static StartupConfiguration Resolve(string[] args) {
        return Resolve(args,DiscoverExecutables(),new[]{Environment.GetEnvironmentVariable("CODEX_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex")});
    }
    internal static StartupConfiguration Resolve(string[] args,IEnumerable<string> defaults,IEnumerable<string> homes) {
        string executable=null,home=null;
        if(args.Length==2 && !args[0].StartsWith("--",StringComparison.Ordinal)) {
            executable=args[0];home=args[1];
        }else {
            for(int i=0;i<args.Length;i+=2) {
                if(i+1>=args.Length || String.IsNullOrWhiteSpace(args[i+1]))throw Usage();
                if(args[i]=="--codex-executable" && executable==null)executable=args[i+1];
                else if(args[i]=="--data-directory" && home==null)home=args[i+1];
                else throw Usage();
            }
        }
        return FromCandidates(executable==null ? defaults : new[]{executable},home==null ? homes : new[]{home});
    }
    static ArgumentException Usage() {
        return new ArgumentException("用法：PetFolio.exe [Codex CLI 路徑] [Codex 資料目錄]，或使用 --codex-executable 路徑、--data-directory 路徑個別指定。");
    }
    static IEnumerable<string> DiscoverExecutables() {
            foreach(var process in Process.GetProcessesByName("codex")) {
                string candidate=null;
                using(process) try {
                    var path=process.MainModule.FileName;
                    if(path.IndexOf("OpenAI\\Codex\\bin\\",StringComparison.OrdinalIgnoreCase)>=0) candidate=path;
                }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                if(candidate!=null)yield return candidate;
            }
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if(Directory.Exists(root)) {
                var files=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories);
                Array.Sort(files,(a,b)=>File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                foreach(var file in files)yield return file;
            }
            foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
                string candidate=null;
                if(!String.IsNullOrWhiteSpace(directory)) try {candidate=Path.Combine(directory.Trim('"'),"codex.exe");}catch(ArgumentException){}
                if(candidate!=null)yield return candidate;
            }
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
