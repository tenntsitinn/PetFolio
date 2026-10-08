using System;
using System.IO;

static class StartupTests {
    static int checks;
    static void Check(bool value,string name) {if(!value)throw new Exception(name);checks++;}
    static void Main() {
        var root=Path.Combine(Path.GetTempPath(),"PetFolio-startup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var cli=Path.Combine(root,"codex.exe");File.WriteAllText(cli,"");
            var home=Path.Combine(root,"custom home");Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home,".codex-global-state.json"),"{}");
            var missing=Path.Combine(root,"missing");
            var resolved=StartupConfiguration.FromCandidates(new[]{missing,cli},new[]{null,missing,home});
            Check(resolved.Executable==cli,"Skip missing CLI candidates");
            Check(resolved.Home==home,"Skip missing desktop data candidates");
            var fallback=Path.Combine(root,"fallback");Directory.CreateDirectory(fallback);
            File.WriteAllText(Path.Combine(fallback,".codex-global-state.json"),"{}");
            Check(StartupConfiguration.FromCandidates(new[]{cli},new[]{home,fallback}).Home==home,"Prefer configured home");
            Check(StartupConfiguration.Resolve(new[]{cli,home}).Home==home,"Explicit paths support spaces");
            bool rejected=false;try{StartupConfiguration.FromCandidates(new[]{missing},new[]{home});}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Missing CLI produces actionable failure");
            rejected=false;try{StartupConfiguration.FromCandidates(new[]{cli},new[]{missing});}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Missing desktop data produces actionable failure");
            rejected=false;try{StartupConfiguration.Resolve(new[]{cli});}catch(ArgumentException){rejected=true;}
            Check(rejected,"Incomplete explicit arguments rejected");
            var customHome=StartupConfiguration.Resolve(new[]{"--data-directory",home},new[]{cli},new[]{fallback});
            Check(customHome.Executable==cli && customHome.Home==home,"A desktop shortcut keeps its custom home while discovering the current CLI");
            var newerCli=Path.Combine(root,"new version","codex.exe");Directory.CreateDirectory(Path.GetDirectoryName(newerCli));File.WriteAllText(newerCli,"");
            File.Delete(cli);
            var upgraded=StartupConfiguration.Resolve(new[]{"--data-directory",home},new[]{cli,newerCli},new[]{fallback});
            Check(upgraded.Executable==newerCli && upgraded.Home==home,"CLI upgrades do not break a shortcut with a custom data directory");
            var customCli=StartupConfiguration.Resolve(new[]{"--codex-executable",newerCli},new[]{missing},new[]{home});
            Check(customCli.Executable==newerCli && customCli.Home==home,"CLI-only override discovers desktop data");
            Check(StartupConfiguration.Resolve(new[]{"--data-directory",home,"--codex-executable",newerCli},new[]{missing},new[]{missing}).Executable==newerCli,"Both named overrides work in either order");
            rejected=false;try{StartupConfiguration.Resolve(new[]{cli,home},new[]{newerCli},new[]{home});}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Explicit stale CLI paths fail rather than silently choosing a different installation");
            foreach(var invalid in new[]{new[]{"--data-directory"},new[]{"--unknown",home},new[]{"--data-directory",home,"--data-directory",fallback},new[]{"--codex-executable"," "}}) {
                rejected=false;try{StartupConfiguration.Resolve(invalid,new[]{newerCli},new[]{home});}catch(ArgumentException){rejected=true;}
                Check(rejected,"Invalid named options produce an actionable usage error");
            }
            Console.WriteLine("PASS "+checks+" checks");
        }finally {Directory.Delete(root,true);}
    }
}
