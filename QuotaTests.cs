using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class QuotaTests {
    static int checks;
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);checks++;}
    static QuotaSnapshot Parse(string text) {
        return QuotaResponse.Parse(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text),DateTime.UtcNow);
    }
    static void Reject(string text) {
        bool rejected=false;try{Parse(text);}catch(FormatException){rejected=true;}
        Check(rejected,"Malformed/absent quota must fail instead of claiming a fresh snapshot");
    }
    sealed class Source : IQuotaSource {
        public int Calls,Disposals;
        public readonly AutoResetEvent Entered=new AutoResetEvent(false);
        public readonly AutoResetEvent Continue=new AutoResetEvent(false);
        public readonly AutoResetEvent Returned=new AutoResetEvent(false);
        public Exception Error;
        public QuotaSnapshot Result=new QuotaSnapshot(DateTime.UtcNow,new QuotaWindow(23,300,1234),null);
        public QuotaSnapshot Read() {
            Interlocked.Increment(ref Calls);Entered.Set();
            if(!Continue.WaitOne(5000))throw new TimeoutException("Test source was not released");
            Returned.Set();if(Error!=null)throw Error;return Result;
        }
        public void Dispose(){Interlocked.Increment(ref Disposals);Continue.Set();}
    }
    sealed class DispatchQueue {
        readonly ConcurrentQueue<Action> pending=new ConcurrentQueue<Action>();
        readonly AutoResetEvent posted=new AutoResetEvent(false);
        public void Post(Action action){pending.Enqueue(action);posted.Set();}
        public void Wait(){if(!posted.WaitOne(5000))throw new Exception("Missing completion callback");}
        public void Drain(){Action action;while(pending.TryDequeue(out action))action();}
    }
    [STAThread] static void Main(string[] args) {
        var snapshot=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":23.5,\"windowDurationMins\":300,\"resetsAt\":1800000000},\"secondary\":{\"usedPercent\":80,\"windowDurationMins\":10080},\"credits\":{\"balance\":999}}}");
        Check(snapshot.Primary.RemainingPercent==76.5 && snapshot.Secondary.RemainingPercent==20,"Subscription percentages remain separate from credits");
        Check(snapshot.Primary.DurationMinutes==300 && snapshot.Primary.ResetsAt==1800000000,"Durations and reset times survive parsing for future consumers");
        snapshot=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":90}},\"codex\":{\"primary\":{\"usedPercent\":10}}}}");
        Check(snapshot.Primary.RemainingPercent==90,"Codex bucket takes precedence");
        Check(snapshot.Secondary==null,"Missing secondary window is supported");
        Check(new QuotaWindow(-10,300,null).RemainingPercent==100 && new QuotaWindow(120,300,null).RemainingPercent==0,"Remaining display clamps to 0-100");
        Reject("{}");Reject("{\"rateLimitsByLimitId\":{\"other\":{}}}");Reject("{\"rateLimits\":{}}");
        Reject("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"NaN\"}}}");
        var source=new Source();var queue=new DispatchQueue();int changes=0,writes=0;
        var service=new QuotaService(source,queue.Post,s=>writes++,(k,d)=>{});
        service.Changed+=s=>changes++;
        service.Refresh();Check(source.Entered.WaitOne(5000),"First refresh starts");
        Check(service.State.IsRefreshing && changes==1,"Refresh publishes immediate busy feedback");
        service.Refresh();service.Refresh();Check(source.Calls==1,"Overlapping requests coalesce");
        source.Continue.Set();queue.Wait();queue.Drain();
        Check(changes==2 && writes==1 && service.State.Snapshot==source.Result && !service.State.IsStale && !service.State.IsRefreshing,"Successful data publishes and persists once and clears busy state");
        source.Error=new Exception("offline");service.Refresh();Check(source.Entered.WaitOne(5000),"Retry starts after success");
        Check(service.State.IsRefreshing && service.State.Snapshot==source.Result,"Refresh preserves displayed quota while loading");
        source.Continue.Set();queue.Wait();queue.Drain();
        Check(service.State.IsStale && service.State.Snapshot==source.Result && writes==1 && !service.State.IsRefreshing,"Failure preserves the last good snapshot, clears busy state and marks it stale");
        source.Error=null;service.Refresh();Check(source.Entered.WaitOne(5000),"Retry starts after failure");
        source.Continue.Set();queue.Wait();
        int before=changes;service.Dispose();service.Dispose();queue.Drain();service.Refresh();
        Check(changes==before && writes==1,"Already queued results cannot publish after disposal");
        Check(source.Disposals==1 && source.Calls==3,"Disposal is idempotent and blocks future refreshes");
        source=new Source();queue=new DispatchQueue();
        using(var failing=new QuotaService(source,queue.Post,s=>{},(k,d)=>{})) {
            source.Error=new Exception("offline");failing.Refresh();Check(source.Entered.WaitOne(5000),"Initial error request starts");
            source.Continue.Set();queue.Wait();queue.Drain();
            Check(failing.State.Snapshot==null && failing.State.Error!=null && !failing.State.IsStale,"Initial failure has no invented cached data");
            source.Error=null;failing.Refresh();Check(source.Entered.WaitOne(5000),"Recovery starts");
            source.Continue.Set();queue.Wait();queue.Drain();
            Check(failing.State.Error==null && failing.State.Snapshot!=null,"Successful retry clears error state");
        }
        source=new Source();queue=new DispatchQueue();int persistenceErrors=0;
        using(var resilient=new QuotaService(source,queue.Post,s=>{throw new System.IO.IOException();},(k,d)=>{if(k=="quota-snapshot-error")persistenceErrors++;})) {
            resilient.Refresh();Check(source.Entered.WaitOne(5000),"Persistence failure request starts");
            source.Continue.Set();queue.Wait();queue.Drain();
            Check(resilient.State.Snapshot!=null && resilient.State.Error==null && persistenceErrors==1,"Disk failure does not discard fresh quota");
        }
        using(var closed=new CodexQuotaSource("does-not-exist.exe")) {
            closed.Dispose();bool rejected=false;
            try{closed.Read();}catch(ObjectDisposedException){rejected=true;}
            Check(rejected,"Disposed source cannot launch a process");
        }
        if(args.Length>0) {
            Transport(args[0]);
            var previousEncoding=Console.InputEncoding;
            try {
                Console.InputEncoding=new UTF8Encoding(true);
                Transport(args[0]);
            }finally {Console.InputEncoding=previousEncoding;}
        }
        Console.WriteLine(checks+" quota service checks passed");
    }
    static void Transport(string executable) {
        var folder=Path.GetDirectoryName(executable);
        var mode=Path.Combine(folder,"fake-quota-mode.txt");var pidFile=Path.Combine(folder,"fake-quota-pid.txt");
        try {
            File.WriteAllText(mode,"success");
            using(var source=new CodexQuotaSource(executable)) {
                Check(source.Read().Primary.RemainingPercent==83,"Real stdio handshake ignores notifications, malformed lines and unrelated errors");
            }
            File.WriteAllText(mode,"reject");
            using(var source=new CodexQuotaSource(executable)) {
                bool rejected=false;try{source.Read();}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"CLI protocol rejection becomes a failed query");
            }
            File.WriteAllText(mode,"stall");
            using(var source=new CodexQuotaSource(executable)) {
                var reading=Task.Factory.StartNew(()=>{try{source.Read();return false;}catch(Exception){return true;}});
                Check(SpinWait.SpinUntil(()=>File.Exists(pidFile) && new FileInfo(pidFile).Length>0,5000),"In-flight query reaches the child process");
                int pid=Int32.Parse(File.ReadAllText(pidFile));
                source.Dispose();
                Check(reading.Wait(5000) && reading.Result,"Disposal interrupts a blocked quota read promptly");
                bool exited;try{using(var child=Process.GetProcessById(pid))exited=child.WaitForExit(5000);}catch(ArgumentException){exited=true;}
                Check(exited,"Disposal leaves no quota child process running");
            }
        }finally {if(File.Exists(mode))File.Delete(mode);if(File.Exists(pidFile))File.Delete(pidFile);}
    }
}
