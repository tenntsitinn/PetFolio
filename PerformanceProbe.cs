using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Isolated production tracker/view/services with fixture pet discovery and quota.
// Does not query accounts, move the cursor or edit any Codex configuration.
static class PerformanceProbe {
    sealed class PetWindow : Form {protected override bool ShowWithoutActivation {get{return true;}}}
    sealed class Quota : IQuotaSource {
        public QuotaSnapshot Read(){return new QuotaSnapshot(DateTime.UtcNow,new QuotaWindow(20,300,null),new QuotaWindow(30,10080,null));}
        public void Dispose(){}
    }
    static readonly JavaScriptSerializer json=new JavaScriptSerializer();
    static void Pump(double seconds) {
        var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){Application.DoEvents();Thread.Sleep(5);}
    }
    [STAThread] static void Main(string[] args) {
        try {Run(args);}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    }
    static void Run(string[] args) {
        if(args.Length<1)throw new ArgumentException("Pass an output JSON file and optional seconds per scenario (default 8).");
        double seconds=args.Length>1?Double.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture):8;
        if(seconds<2 || seconds>30)throw new ArgumentException("Seconds must be between 2 and 30.");
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"probe-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("PETFOLIO_DATA_DIR",root);
        string stateFile=Path.Combine(root,".codex-global-state.json");
        File.WriteAllText(Path.Combine(root,"config.toml"),"appearanceTheme = \"dark\"\n[desktop]\nselected-avatar-id = \"\"\n");
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var results=new List<object>();
        using(var target=new PetWindow())using(var dispatcher=new Control())using(var motion=new System.Windows.Forms.Timer()) {
            var area=Screen.PrimaryScreen.WorkingArea;var origin=new Point(area.Left+area.Width/2-100,area.Top+area.Height/2-50);
            target.FormBorderStyle=FormBorderStyle.None;target.ShowInTaskbar=false;target.TopMost=true;
            target.BackColor=Color.Goldenrod;target.Bounds=new Rectangle(origin,new Size(112,122));target.Show();
            IntPtr petHandle=target.Handle;var dispatchHandle=dispatcher.Handle;bool missing=false;
            Action<Point,bool> writeState=(point,open)=>File.WriteAllText(stateFile,json.Serialize(new Dictionary<string,object>{
                {"electron-avatar-overlay-open",open},{"electron-avatar-overlay-bounds",new {x=point.X,y=point.Y}}}));
            writeState(origin,true);
            using(var pets=new PetStateService(root,Path.Combine(root,"pet-palettes.json"),a=>dispatcher.BeginInvoke(a),
                ()=>missing?new List<IntPtr>():new List<IntPtr>{petHandle}))
            using(var feature=new QuotaFeature(root,pets,()=>new Quota(),a=>dispatcher.BeginInvoke(a),s=>{},Program.Record)) {
                pets.Start();feature.Start();int step=0;motion.Interval=100;
                motion.Tick+=(s,e)=>{step++;var point=new Point(origin.X+(int)(80*Math.Sin(step*.3)),origin.Y+(int)(25*Math.Cos(step*.3)));writeState(point,true);};
                Action<string> measure=name=> {
                    Pump(2);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    using(var process=Process.GetCurrentProcess()) {
                        var cpu=process.TotalProcessorTime;long selections=pets.SelectionReads,states=pets.StateReads,publications=pets.Publications;
                        long frames=feature.View.FramesRendered,backgrounds=feature.View.BackgroundReads;int collections=GC.CollectionCount(0);
                        var clock=Stopwatch.StartNew();Pump(seconds);double elapsed=clock.Elapsed.TotalSeconds;process.Refresh();
                        results.Add(new {scenario=name,seconds=elapsed,cpuCorePercent=100*(process.TotalProcessorTime-cpu).TotalSeconds/elapsed,
                            workingSetMiB=process.WorkingSet64/1048576.0,privateMiB=process.PrivateMemorySize64/1048576.0,
                            managedKiB=GC.GetTotalMemory(false)/1024.0,gen0Collections=GC.CollectionCount(0)-collections,
                            selectionReads=pets.SelectionReads-selections,stateReads=pets.StateReads-states,
                            publications=pets.Publications-publications,frames=feature.View.FramesRendered-frames,
                            backgroundReads=feature.View.BackgroundReads-backgrounds,systemSampling=feature.SystemService.IsRunning});
                        Console.WriteLine("Measured "+name);
                    }
                };
                measure("idle-quota");motion.Start();measure("moving-quota");motion.Stop();writeState(origin,true);
                feature.View.SetDefaultAppearance(false);
                typeof(QuotaLabel).GetMethod("SetOpacity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(feature.View,new object[]{20});
                feature.View.SetMode(true);measure("system-low-opacity");
                feature.View.SetBubbleVisible(false);measure("hidden-bubble");
                missing=true;writeState(origin,false);measure("pet-closed");
            }
        }
        File.WriteAllText(Path.GetFullPath(args[0]),json.Serialize(new {at=DateTime.UtcNow.ToString("o"),windows=PetDiagnostics.WindowsVersion,
            logicalProcessors=Environment.ProcessorCount,scenario="Fixture discovery, real Windows tracker/view/composition/OS counters; CPU is percent of one core; warmup 2s per phase",results=results}));
        foreach(var file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);
    }
}
