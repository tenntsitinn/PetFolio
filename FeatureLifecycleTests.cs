using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

static class FeatureLifecycleTests {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    sealed class Pets : IPetStateSource {
        Action<PetState> changed;
        public PetState Current {get{return PetState.Empty;}}
        public int Subscribers {get{return changed==null?0:changed.GetInvocationList().Length;}}
        public event Action<PetState> Changed {add{changed+=value;}remove{changed-=value;}}
        public void Tick(){if(changed!=null)changed(Current);}
    }
    sealed class Source : IQuotaSource {
        public int Disposals,Calls;
        public QuotaSnapshot Read(){Interlocked.Increment(ref Calls);return new QuotaSnapshot(DateTime.UtcNow,new QuotaWindow(20,300,null),null);}
        public void Dispose(){Disposals++;}
    }
    [STAThread] static void Main() {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var pets=new Pets();var sources=new List<Source>();var callbacks=new ConcurrentQueue<Action>();
        using(var posted=new AutoResetEvent(false))
        using(var feature=new QuotaFeature("",pets,()=>{var s=new Source();sources.Add(s);return s;},
            action=>{callbacks.Enqueue(action);posted.Set();},s=>{},(k,d)=>{})) {
            Check(!feature.IsRunning && pets.Subscribers==0,"Construction owns no live feature resources");
            feature.Start();feature.Start();
            Check(posted.WaitOne(5000),"Quota request completes without depending on a visible view");
            Check(sources.Count==1 && pets.Subscribers==1 && feature.Commands.Length==3,"Start is idempotent and registers one subscriber");
            var first=feature.View;var firstButton=first.DismissButton;
            bool glassAvailable=first.Owner is GlassBackdrop && ((GlassBackdrop)first.Owner).Ready;
            feature.View.SetBubbleVisible(false);pets.Tick();
            Check(feature.IsRunning && sources[0].Disposals==0 && !feature.View.BubbleRequested,"Hiding the bubble preserves the running feature");
            feature.Restore();Check(feature.View==first && feature.View.BubbleRequested,"Restore reuses the live view");
            feature.Stop();feature.Stop();
            Check(!feature.IsRunning && first.IsDisposed && firstButton.IsDisposed,"Stop disposes the view and child surfaces");
            Check(pets.Subscribers==0 && sources[0].Disposals==1 && feature.Commands.Length==0,"Stop releases query source, subscription and commands");
            feature.Start();Check(posted.WaitOne(5000),"Restart launches a fresh request");
            Check(sources.Count==2 && pets.Subscribers==1 && feature.View!=first,"Restart creates a fresh feature instance without duplicating subscribers");
            Check(!glassAvailable || (feature.View.Owner is GlassBackdrop && ((GlassBackdrop)feature.View.Owner).Ready),"Restart preserves available glass instead of silently falling back");
            Action callback;while(callbacks.TryDequeue(out callback))callback();
            Check(!feature.View.IsDisposed,"Old queued completions are harmless after restart");
            for(int i=0;i<3;i++) {
                feature.Stop();feature.Start();Check(posted.WaitOne(5000),"Repeated restart completes");
                while(callbacks.TryDequeue(out callback))callback();
                Check(pets.Subscribers==1,"Repeated restarts retain exactly one pet subscription");
                Check(!glassAvailable || (feature.View.Owner is GlassBackdrop && ((GlassBackdrop)feature.View.Owner).Ready),"Every restart preserves available glass");
            }
            using(var sibling=new QuotaFeature("",pets,()=>new Source(),action=>{},s=>{},(k,d)=>{})) {
                sibling.Start();Check(pets.Subscribers==2,"Two features share one pet state source");
                Check(!glassAvailable || (sibling.View.Owner is GlassBackdrop && ((GlassBackdrop)sibling.View.Owner).Ready),"Concurrent feature surfaces share the composition dispatcher");
                feature.Stop();pets.Tick();
                Check(sibling.IsRunning && pets.Subscribers==1,"Stopping one feature leaves its sibling running");
            }
            feature.Dispose();Check(pets.Subscribers==0 && !feature.IsRunning,"Final disposal releases the feature");
            foreach(var source in sources)Check(source.Disposals==1,"Each source is disposed exactly once");
            bool rejected=false;try{feature.Start();}catch(ObjectDisposedException){rejected=true;}
            Check(rejected,"Final disposal cannot be restarted");
        }
        Console.WriteLine(checks+" feature lifecycle checks passed");
    }
}
