using System;using System.Drawing;using System.IO;using System.Web.Script.Serialization;
class PetSwitchTests {
 static int checks;
 static void Check(bool ok,string message){if(!ok){Console.WriteLine("FAIL "+message);Environment.Exit(1);}checks++;Console.WriteLine("PASS "+message);}
 static void Main(string[] args) {
  var state=new PetColourSwitch();state.Select("A",0);long first=state.Generation;
  state.Select("B",10);long second=state.Generation;state.Select("A",20);long third=state.Generation;
  Check(!state.AcceptSample(first,Color.Red,900) && !state.HasColour,"A -> B -> A rejects the first A result");
  Check(!state.ApplySource(second,Color.Blue),"Stale B source cannot overwrite the current A");
  Check(state.ApplySource(third,Color.Gold) && state.HasColour,"Latest source wins even while old captures are outstanding");
  Check(!state.Select("A",30) && state.Generation==third,"Same pet keeps its fixed colour and generation");
  Check(!state.AcceptSample(first,Color.Red,2000),"Late capture cannot replace source-derived colour");
  state.Select("Unknown",1000);long unknown=state.Generation;
  Check(!state.CanSample(1349) && state.CanSample(1350),"Unknown pet waits for the selection transition");
  Check(!state.AcceptSample(unknown,Color.Blue,1350),"One early frame cannot lock a colour");
  Check(!state.AcceptSample(unknown,Color.Blue,1500),"Two early frames cannot lock a colour");
  Check(!state.AcceptSample(unknown,null,1650),"Empty capture restarts frame confirmation");
  Check(!state.AcceptSample(unknown,Color.Red,1800) && !state.AcceptSample(unknown,Color.Blue,1950),"Changing frame colours do not settle");
  Check(!state.AcceptSample(unknown,Color.Blue,2100) && state.AcceptSample(unknown,Color.Blue,2250),"Three consistent late frames settle the fallback");
  var rapid=new PetColourSwitch();for(int i=0;i<1000;i++)rapid.Select("pet"+i,i);
  Check(!rapid.ApplySource(999,Color.Red) && rapid.ApplySource(1000,Color.Gold),"1000 rapid choices accept only the latest generation");
  var folder=Path.Combine(Path.GetTempPath(),"pet-switch-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try {
   var config=Path.Combine(folder,"config.toml");
   File.WriteAllText(config,"# selected-avatar-id = \"wrong\"\n[other]\nselected-avatar-id = \"also-wrong\"\n[desktop]\n\"selected-avatar-id\" = \"A\" # comment\n");
   Check(PetSelectionConfig.Read(config)=="A","Only the real desktop selection is parsed");
   File.WriteAllText(config,"[desktop]\nselected-avatar-id = \"unfinished");
   Check(PetSelectionConfig.Read(config)==null && !rapid.Select(PetSelectionConfig.Read(config),3000),"Partial config writes preserve the last valid selection");
   if(args.Length>0) {
    var catalog=new PetPaletteCatalog(args[0]);
    foreach(var pet in new[]{"codex","hoots","seedy","stacky","null-signal","bsod","rocky","dewey","fireball","custom:naiwa","pet_6abb27c186a88191912860057e05d0d8"}) {
     Color c;Check(catalog.TryGet(pet,out c),"Source palette is valid for "+pet);
    }
    Color missing;Check(!catalog.TryGet("unknown",out missing),"Unlisted pet uses fallback instead of someone else's colour");
   }
   var sprite=Path.Combine(folder,"sprite.fake");File.WriteAllText(sprite,"old");var info=new FileInfo(sprite);
   var cached=Path.Combine(folder,"palette.json");File.WriteAllText(cached,new JavaScriptSerializer().Serialize(new{entries=new{test=new{r=10,g=20,b=30,sourcePath=sprite,sourceBytes=info.Length,lastWriteUtcTicks=info.LastWriteTimeUtc.Ticks.ToString()}}}));
   var local=new PetPaletteCatalog(cached);Color found;Check(local.TryGet("test",out found),"Matching source fingerprint is accepted");
   File.AppendAllText(sprite,"changed");Check(!local.TryGet("test",out found),"Changed sprite invalidates its palette");
  }finally {foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
  Console.WriteLine(checks+" pet switching checks passed");
 }
}
