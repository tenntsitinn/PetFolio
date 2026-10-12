using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

static class SystemMetricsServiceTests {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    sealed class Source : ISystemMetricsSource {
        internal int Reads;internal bool Fail;
        public SystemMetrics Read() {
            Reads++;if(Fail)throw new InvalidOperationException("Unavailable counters");
            return new SystemMetrics {CpuPercent=Reads==1?(double?)null:42,MemoryPercent=30};
        }
    }
    [STAThread] static void Main() {
        var sources=new List<Source>();int errors=0,changes=0;bool manual=false;SystemMetrics latest=null;
        using(var service=new SystemMetricsService(()=>{var source=new Source();sources.Add(source);return source;},(k,d)=>errors++)) {
            service.Changed+=(metrics,isManual)=>{changes++;latest=metrics;manual=isManual;};
            var timer=(Timer)typeof(SystemMetricsService).GetField("sampleTimer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(service);
            Action tick=()=>typeof(Timer).GetMethod("OnTick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(timer,new object[]{EventArgs.Empty});
            Check(!service.IsRunning && !timer.Enabled && sources.Count==0,"Construction starts no sampling");
            tick();Check(changes==0,"Stopped timer callbacks never read counters");
            service.SetRunning(true);Check(service.IsRunning && timer.Enabled && changes==1 && !manual && !latest.CpuPercent.HasValue,"Start immediately establishes a fresh baseline");
            service.SetRunning(true);Check(changes==1 && sources.Count==1,"Repeated start does not duplicate sampling");
            tick();Check(changes==2 && latest.CpuPercent==42,"Next timer sample uses the existing baseline");
            service.Refresh();Check(changes==3 && manual && sources[0].Reads==3,"Visible manual refresh shares the active source");
            service.SetRunning(false);service.SetRunning(false);tick();Check(!timer.Enabled && changes==3,"Stop is idempotent and suppresses queued ticks");
            service.Refresh();Check(changes==4 && manual && sources.Count==2 && !latest.CpuPercent.HasValue && !service.IsRunning,"Hidden manual refresh takes one sample without starting polling");
            service.Refresh();Check(sources.Count==3 && !latest.CpuPercent.HasValue,"Hidden refresh cannot retain a rate baseline");
            service.SetRunning(true);Check(sources.Count==4 && !latest.CpuPercent.HasValue,"Resume discards pre-hide counters");
            sources[3].Fail=true;tick();Check(errors==1 && !latest.MemoryPercent.HasValue,"Sampling error publishes unavailable values and records failure");
            tick();Check(sources.Count==5 && latest.MemoryPercent==30 && !latest.CpuPercent.HasValue,"Next sample recovers with a fresh source");
            int before=changes;service.Dispose();service.Dispose();service.SetRunning(true);service.Refresh();tick();
            Check(!service.IsRunning && changes==before,"Final disposal prevents future work and callbacks");
        }
        Console.WriteLine(checks+" system sampling lifecycle checks passed");
    }
}
