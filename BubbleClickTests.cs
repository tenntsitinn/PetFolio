using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Exercise production mouse handlers on an independent window without moving
// the user's cursor, generating system input or reading a real account.
static class BubbleClickTests {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    sealed class Pets : IPetStateSource {
        public PetState Current {get{return new PetState(new IntPtr(1),new Point(600,400),112,true,true,false,"test",Color.Gold);}}
        public event Action<PetState> Changed {add{}remove{}}
    }
    sealed class Source : IQuotaSource {
        public int Calls;
        public readonly AutoResetEvent Entered=new AutoResetEvent(false),Release=new AutoResetEvent(true);
        public bool Fail;
        public QuotaSnapshot Read() {
            Interlocked.Increment(ref Calls);Entered.Set();
            if(!Release.WaitOne(5000))throw new Exception("Test did not release source");
            if(Fail)throw new Exception("Offline");
            return new QuotaSnapshot(new DateTime(2026,10,7,1,2,3,DateTimeKind.Utc),new QuotaWindow(20,300,null),null);
        }
        public void Dispose(){Release.Set();}
    }
    static void Mouse(QuotaLabel view,string method,int x,int y,MouseButtons button=MouseButtons.Left) {
        typeof(QuotaLabel).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(view,new object[]{new MouseEventArgs(button,1,x,y,0)});
    }
    static string Text(QuotaLabel view,string field) {
        return (string)typeof(QuotaLabel).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
    }
    static void Click(QuotaLabel view,int x=80,int y=40) {
        var point=new IntPtr(x|(y<<16));
        SendMessage(view.Handle,0x201,new IntPtr(1),point);
        SendMessage(view.Handle,0x202,IntPtr.Zero,point);
    }
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    [STAThread] static void Main() {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var source=new Source();var pending=new ConcurrentQueue<Action>();
        using(var posted=new AutoResetEvent(false))
        using(var feature=new QuotaFeature("",new Pets(),()=>source,a=>{pending.Enqueue(a);posted.Set();},s=>{},(k,d)=>{})) {
            Action drain=()=>{Check(posted.WaitOne(5000),"Query completion arrives");Action a;while(pending.TryDequeue(out a))a();};
            feature.Start();Check(source.Entered.WaitOne(5000),"Initial request starts");drain();
            var view=feature.View;int clicks=0;view.RefreshRequested+=()=>clicks++;
            var amount=Text(view,"value1");
            Click(view);Check(source.Entered.WaitOne(5000),"Single click starts a real feature refresh");
            Check(clicks==1 && Text(view,"status")=="Updating..." && Text(view,"value1")==amount,"Click shows busy feedback while preserving the amount");
            Click(view);Click(view);
            Check(source.Calls==2,"Repeated clicks during refresh do not start extra queries");
            source.Release.Set();drain();
            Check(Text(view,"status")=="updated "+new DateTime(2026,10,7,1,2,3,DateTimeKind.Utc).ToLocalTime().ToString("HH:mm"),"Successful refresh displays hours and minutes");
            int before=clicks;
            Mouse(view,"OnMouseDown",80,40);Mouse(view,"OnMouseMove",90,40);Mouse(view,"OnMouseMove",80,40);Mouse(view,"OnMouseUp",80,40);
            Check(clicks==before,"Dragging away and back never becomes a click");
            Mouse(view,"OnMouseDown",80,40);view.Capture=false;Mouse(view,"OnMouseUp",80,40);
            Check(clicks==before,"Capture loss cancels refresh");
            Mouse(view,"OnMouseDown",80,40,MouseButtons.Right);Mouse(view,"OnMouseUp",80,40,MouseButtons.Right);
            Check(clicks==before,"Right click does not refresh");
            Mouse(view,"OnMouseDown",80,40);Mouse(view,"OnMouseUp",-1,40);
            Check(clicks==before,"Release outside the body does not refresh");
            Mouse(view,"OnMouseDown",0,22);Mouse(view,"OnMouseUp",0,19);
            Check(clicks==before,"Release outside the rounded corner does not refresh even below the drag threshold");
            view.PreviewHover(new Point(view.Left+80,view.Top+40));
            SendMessage(view.DismissButton.Handle,0x201,new IntPtr(1),new IntPtr(10|(10<<16)));
            SendMessage(view.DismissButton.Handle,0x202,IntPtr.Zero,new IntPtr(10|(10<<16)));
            Check(!view.BubbleRequested && clicks==before,"Close button hides without refreshing");
            feature.Restore();
            source.Fail=true;
            Mouse(view,"OnMouseDown",80,40);Mouse(view,"OnMouseMove",83,43);Mouse(view,"OnMouseUp",83,43);
            Check(source.Entered.WaitOne(5000) && clicks==before+1,"Sub-threshold pointer jitter still refreshes");
            source.Release.Set();drain();
            Check(Text(view,"status").StartsWith("Failed · last ") && Text(view,"value1")==amount,"Failure retains amount and shows last successful time");
            source.Fail=false;Click(view);Check(source.Entered.WaitOne(5000),"Failed refresh can be retried by clicking");
            source.Release.Set();drain();Check(Text(view,"status").StartsWith("updated "),"Retry clears error feedback");
        }
        Console.WriteLine(checks+" bubble click checks passed");
    }
}
