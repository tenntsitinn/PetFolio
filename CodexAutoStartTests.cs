using System;
using System.IO;
static class CodexAutoStartTests {
    static int checks;
    static void Check(bool value){if(!value)throw new Exception("Auto-start check failed: "+checks);checks++;}
    static void Main() {
        Check(CodexDesktopSession.Matches("ChatGPT",@"C:\Program Files\WindowsApps\OpenAI.Codex_1\ChatGPT.exe"));
        Check(!CodexDesktopSession.Matches("codex",@"C:\OpenAI\Codex\bin\codex.exe"));
        Check(!CodexDesktopSession.Matches("ChatGPT",@"C:\OtherApp\ChatGPT.exe"));
        Check(!CodexAutoStart.ShouldLaunch("","",false));
        Check(CodexAutoStart.ShouldLaunch("100:1","",false));
        Check(!CodexAutoStart.ShouldLaunch("100:1","100:1",false));
        Check(CodexAutoStart.ShouldLaunch("100:2","100:1",false));
        Check(!CodexAutoStart.ShouldLaunch("100:2","",true));
        Check(CodexAutoStart.Quote(@"C:\含 空格\PetFolio.exe")=="\"C:\\含 空格\\PetFolio.exe\"");
        var directory=Path.Combine(Path.GetTempPath(),"petfolio-autostart-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            Check(CodexAutoStart.Read(directory)==null);
            File.WriteAllText(CodexAutoStart.SettingsPath(directory),"{}");Check(CodexAutoStart.Read(directory)==null);
            File.WriteAllText(CodexAutoStart.SettingsPath(directory),"invalid");Check(CodexAutoStart.Read(directory)==null);
            File.WriteAllText(CodexAutoStart.SettingsPath(directory),"{\"Executable\":\"C:\\\\App.exe\",\"DesktopHome\":\"C:\\\\home\",\"DataDirectory\":\"C:\\\\data\"}");
            Check(CodexAutoStart.Read(directory).CliHome==null);
        }finally {File.Delete(CodexAutoStart.SettingsPath(directory));Directory.Delete(directory);}
        Console.WriteLine("PASS "+checks+" auto-start policy checks (no registry writes)");
    }
}
