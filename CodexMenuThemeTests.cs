using System;
using System.IO;
static class CodexMenuThemeTests {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static void Main() {
        string root=Path.Combine(Path.GetTempPath(),"PetFolio-menu-theme-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string path=Path.Combine(root,"config.toml");
        try {
            bool? system=false;Func<bool?> read=()=>system;
            Check(CodexMenuTheme.Read(root,read)==false,"Missing theme follows the light system");
            File.WriteAllText(path,"appearanceTheme = \"dark\"\n");
            Check(CodexMenuTheme.Read(root,read)==true,"Explicit Codex dark overrides a light system");
            system=true;File.WriteAllText(path,"appearanceTheme = 'light' # user's selection\n");
            Check(CodexMenuTheme.Read(root,read)==false,"Explicit Codex light overrides a dark system");
            File.WriteAllText(path,"appearanceTheme = \"system\"\n");
            Check(CodexMenuTheme.Read(root,read)==true,"System selection follows dark Windows");
            system=false;Check(CodexMenuTheme.Read(root,read)==false,"System changes are read without restarting");
            File.WriteAllText(path,"# appearanceTheme = \"dark\"\nappearanceThemeOther = \"dark\"\n");
            Check(CodexMenuTheme.Read(root,read)==false,"Comments and similarly named settings are ignored");
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check(CodexMenuTheme.Read(root,read)==null,"Locked config reports no new theme so the caller can retain the current palette");
            system=null;Check(CodexMenuTheme.Read(root,read)==null,"Failed system reading preserves caller fallback");
        }finally {File.Delete(path);Directory.Delete(root);}
        Console.WriteLine(checks+" Codex menu theme checks passed");
    }
}
