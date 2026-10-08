using System;
using System.IO;

static class RuntimeDataTests {
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    static void Main() {
        var root=Path.Combine(Path.GetTempPath(),"PetFolio-data-"+Guid.NewGuid().ToString("N"));
        var old=Path.Combine(root,"old plugin");var data=Path.Combine(root,"user data");
        Directory.CreateDirectory(old);
        try {
            Check(RuntimeData.Resolve(null,root)==Path.Combine(root,"PetFolio"),"Default data is outside the executable directory");
            Check(RuntimeData.Resolve(data,root)==data,"Explicit absolute data directory is honored");
            bool rejected=false;try{RuntimeData.Resolve("relative",root);}catch(ArgumentException){rejected=true;}
            Check(rejected,"Relative data override is rejected");
            foreach(var relative in new[]{"C:relative","\\relative"}) {
                rejected=false;try{RuntimeData.Resolve(relative,root);}catch(ArgumentException){rejected=true;}
                Check(rejected,"Drive-relative and root-relative overrides are rejected");
            }
            File.WriteAllText(Path.Combine(old,"appearance.json"),"old preferences");
            File.WriteAllText(Path.Combine(old,"pet-palettes.json"),"old palette");
            File.WriteAllText(Path.Combine(old,"quota-snapshot.json"),"personal quota");
            File.WriteAllText(Path.Combine(old,"probe-events.jsonl"),"personal log");
            RuntimeData.Initialize(data,old);
            Check(File.ReadAllText(Path.Combine(data,"appearance.json"))=="old preferences","First launch migrates preferences");
            Check(File.ReadAllText(Path.Combine(data,"pet-palettes.json"))=="old palette","First launch migrates palette metadata");
            Check(!File.Exists(Path.Combine(data,"quota-snapshot.json")) && !File.Exists(Path.Combine(data,"probe-events.jsonl")),"Old quota and diagnostics are not migrated");
            Check(File.Exists(Path.Combine(old,"appearance.json")),"Migration leaves the original files intact");
            File.WriteAllText(Path.Combine(data,"appearance.json"),"new preferences");
            RuntimeData.Initialize(data,old);
            Check(File.ReadAllText(Path.Combine(data,"appearance.json"))=="new preferences","Repeated launches preserve newer preferences");
            var upgrade=Path.Combine(root,"new plugin");Directory.CreateDirectory(upgrade);
            RuntimeData.Initialize(data,upgrade);
            Check(File.ReadAllText(Path.Combine(data,"appearance.json"))=="new preferences","Replacing the plugin directory preserves user data");
            RuntimeData.Initialize(data,data);
            Check(File.ReadAllText(Path.Combine(data,"appearance.json"))=="new preferences","Using an explicit portable data directory is safe");
            Console.WriteLine("PASS "+checks+" checks");
        }finally{Directory.Delete(root,true);}
    }
}
