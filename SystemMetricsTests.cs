using System;
using System.Threading;

static class SystemMetricsTests {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Main() {
        Check(SystemMetrics.Cpu(120,250,150,100,200,100)==80,"Kernel includes idle time; calculate busy fraction across all processors");
        Check(!SystemMetrics.Cpu(100,200,100,100,200,100).HasValue,"Zero interval is unavailable");
        Check(!SystemMetrics.Cpu(1,2,3,10,20,30).HasValue,"Reset CPU counters need a new baseline");
        Check(SystemMetrics.Speed(4096,1024,2)==1536,"Network rate uses elapsed time");
        Check(!SystemMetrics.Speed(100,200,2).HasValue,"Reset network counters never produce negative speed");
        Check(!SystemMetrics.Speed(100,0,0).HasValue,"Zero elapsed time is unavailable");
        Check(SystemMetrics.Rate(null)=="--" && SystemMetrics.Percent(null)=="--","Missing data is distinct from zero");
        Check(SystemMetrics.Rate(0)=="0 B/s" && SystemMetrics.Rate(1536)=="1.5 KB/s","Scale traffic units");
        Check(SystemMetrics.CompactRate(1.2*1024*1024)=="1.2M" && SystemMetrics.CompactRate(256*1024)=="256K","Compact header preserves units and precision");
        Check(SystemMetrics.CompactRate(null)=="--" && SystemMetrics.CompactRate(0)=="0B","Compact unavailable and zero remain distinct");
        Check(SystemMetrics.CompactRate(1.8*1024*1024,true)=="2M","Overflow header can round without shrinking font");
        var source=new SystemMetricsSource();var first=source.Read();
        Check(!first.CpuPercent.HasValue,"First CPU sample waits for baseline");
        Thread.Sleep(100);var second=source.Read();
        Check(second.CpuPercent.HasValue && second.CpuPercent>=0 && second.CpuPercent<=100,"Native CPU counters produce valid percentage");
        Check(second.MemoryPercent.HasValue && second.MemoryPercent>=0 && second.MemoryPercent<=100,"Native memory counters produce valid percentage");
        Console.WriteLine(checks+" system metrics checks passed");
    }
}
