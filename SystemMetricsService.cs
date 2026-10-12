using System;
using System.Windows.Forms;

// UI-thread sampling lifecycle owned by the feature, independent of its window.
sealed class SystemMetricsService : IDisposable {
    readonly Func<ISystemMetricsSource> createSource;
    readonly Action<string,object> record;
    readonly Timer sampleTimer=new Timer();
    ISystemMetricsSource source;
    bool disposed;
    internal bool IsRunning {get;private set;}
    internal event Action<SystemMetrics,bool> Changed;
    internal SystemMetricsService(Func<ISystemMetricsSource> createSource,Action<string,object> record) {
        this.createSource=createSource;this.record=record;
        sampleTimer.Interval=2000;sampleTimer.Tick+=(s,e)=>{if(IsRunning)Sample(false);};
    }
    internal void SetRunning(bool running) {
        if(disposed || IsRunning==running)return;
        IsRunning=running;
        if(running){Sample(false);if(IsRunning && !disposed)sampleTimer.Start();}
        else {sampleTimer.Stop();source=null;}
    }
    internal void Refresh() {if(!disposed)Sample(true);}
    void Sample(bool manual) {
        SystemMetrics metrics;
        try {if(source==null)source=createSource();metrics=source.Read();}
        catch(Exception e){metrics=new SystemMetrics();source=null;record("system-metrics-error",new {error=e.GetType().Name});}
        // Hidden manual refresh is a single sample, not a new rate baseline.
        if(!IsRunning)source=null;
        var handler=Changed;if(handler!=null)handler(metrics,manual);
    }
    public void Dispose() {
        if(disposed)return;SetRunning(false);disposed=true;
        sampleTimer.Dispose();source=null;Changed=null;
    }
}
