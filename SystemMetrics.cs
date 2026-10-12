using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

sealed class SystemMetrics {
    internal double? CpuPercent, MemoryPercent, DownloadBytesPerSecond, UploadBytesPerSecond;
    internal static string Percent(double? value) {return value.HasValue?value.Value.ToString("0",CultureInfo.InvariantCulture)+"%":"--";}
    internal static string Rate(double? value,bool rounded=false) {
        if(!value.HasValue)return "--";
        double n=Math.Max(0,value.Value);string[] units={"B/s","KB/s","MB/s","GB/s"};int unit=0;
        while(n>=1024 && unit<units.Length-1){n/=1024;unit++;}
        return n.ToString(rounded || unit==0?"0":"0.#",CultureInfo.InvariantCulture)+" "+units[unit];
    }
    internal static string CompactRate(double? value,bool rounded=false) {
        if(!value.HasValue || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value))return "--";
        double n=Math.Max(0,value.Value);string[] units={"B","K","M","G","T"};int unit=0;
        while(n>=1024 && unit<units.Length-1){n/=1024;unit++;}
        return n.ToString(rounded || n>=100 || unit==0?"0":"0.#",CultureInfo.InvariantCulture)+units[unit];
    }
    internal static double? Cpu(ulong idle,ulong kernel,ulong user,ulong oldIdle,ulong oldKernel,ulong oldUser) {
        if(idle<oldIdle || kernel<oldKernel || user<oldUser)return null;
        double total=(double)(kernel-oldKernel)+(user-oldUser);
        return total<=0?(double?)null:Math.Max(0,Math.Min(100,100*(1-(idle-oldIdle)/total)));
    }
    internal static double? Speed(long current,long previous,double seconds) {
        return seconds<=0 || current<previous?(double?)null:(current-previous)/seconds;
    }
}

// Read aggregate OS counters; no WMI, subprocesses or per-process sampling.
interface ISystemMetricsSource {SystemMetrics Read();}

sealed class SystemMetricsSource : ISystemMetricsSource {
    struct Traffic {internal long Received,Sent;internal double At;}
    readonly Dictionary<string,Traffic> traffic=new Dictionary<string,Traffic>();
    readonly Stopwatch clock=Stopwatch.StartNew();
    ulong oldIdle,oldKernel,oldUser;bool cpuKnown;
    public SystemMetrics Read() {
        var result=new SystemMetrics();ulong idle,kernel,user;
        if(GetSystemTimes(out idle,out kernel,out user)) {
            if(cpuKnown)result.CpuPercent=SystemMetrics.Cpu(idle,kernel,user,oldIdle,oldKernel,oldUser);
            oldIdle=idle;oldKernel=kernel;oldUser=user;cpuKnown=true;
        }else cpuKnown=false;
        var memory=new MemoryStatus();memory.Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));
        if(GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical>0)
            result.MemoryPercent=100.0*(memory.TotalPhysical-memory.AvailablePhysical)/memory.TotalPhysical;
        double now=clock.Elapsed.TotalSeconds,download=0,upload=0;bool ready=false,active=false;
        var seen=new HashSet<string>();
        try {
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                if(adapter.OperationalStatus!=OperationalStatus.Up || adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback || adapter.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                try {
                    var stats=adapter.GetIPStatistics();var id=adapter.Id;seen.Add(id);active=true;
                    Traffic old;
                    if(traffic.TryGetValue(id,out old)) {
                        var down=SystemMetrics.Speed(stats.BytesReceived,old.Received,now-old.At);
                        var up=SystemMetrics.Speed(stats.BytesSent,old.Sent,now-old.At);
                        if(down.HasValue && up.HasValue){download+=down.Value;upload+=up.Value;ready=true;}
                    }
                    traffic[id]=new Traffic {Received=stats.BytesReceived,Sent=stats.BytesSent,At=now};
                }catch(NetworkInformationException){}catch(InvalidOperationException){}
            }
            foreach(var id in new List<string>(traffic.Keys))if(!seen.Contains(id))traffic.Remove(id);
            if(ready || !active){result.DownloadBytesPerSecond=download;result.UploadBytesPerSecond=upload;}
        }catch(NetworkInformationException){traffic.Clear();}
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus {
        internal uint Length,Load;
        internal ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
