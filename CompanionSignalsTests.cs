using System;
using System.Threading;

static class CompanionSignalsTests {
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    static void Main() {
        var prefix="Local\\PetFolio-test-"+Guid.NewGuid().ToString("N");
        Check(!CompanionSignals.Running(prefix) && !CompanionSignals.Ready(prefix),"No process reports stopped and not ready");
        CompanionSignals.Stop(prefix);
        Check(!CompanionSignals.Running(prefix),"Stop without a process is harmless");
        using(var started=new ManualResetEvent(false))
        using(var released=new ManualResetEvent(false))
        using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,prefix+"Ready"))
        using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,prefix+"Stop")) {
            var thread=new Thread(()=> {
                using(var mutex=new Mutex(true,prefix)) {
                    started.Set();released.WaitOne();mutex.ReleaseMutex();
                }
            });thread.Start();
            try {
                Check(started.WaitOne(5000),"Host acquires its instance mutex");
                Check(CompanionSignals.Running(prefix) && !CompanionSignals.Ready(prefix),"Initializing is distinct from ready");
                ready.Set();Check(CompanionSignals.Ready(prefix),"Ready is observable from a separate caller");
                CompanionSignals.Stop(prefix);Check(stop.WaitOne(0),"Stop signals the host without killing it");
                ready.Reset();released.Set();Check(thread.Join(5000),"Host releases the instance");
                Check(!CompanionSignals.Running(prefix) && !CompanionSignals.Ready(prefix),"Shutdown reports stopped");
            }finally{released.Set();thread.Join();}
        }
        Console.WriteLine("PASS "+checks+" checks");
    }
}
