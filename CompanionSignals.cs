using System;
using System.Threading;
using System.Text.RegularExpressions;

// Windows session-local control protocol. Tests use a separate name prefix.
static class CompanionSignals {
    internal static string Prefix {
        get {
            var instance=Environment.GetEnvironmentVariable("PETFOLIO_INSTANCE_ID");
            if(String.IsNullOrEmpty(instance))return "Local\\PetFolio";
            if(!Regex.IsMatch(instance,"^[a-zA-Z0-9-]{1,64}$"))
                throw new ArgumentException("PETFOLIO_INSTANCE_ID must contain 1-64 letters, digits, or hyphens.");
            return "Local\\PetFolio-"+instance;
        }
    }
    internal static bool Running(string prefix=null) {
        prefix=prefix??Prefix;
        Mutex mutex;
        if(!Mutex.TryOpenExisting(prefix,out mutex))return false;
        using(mutex) {
            try {
                if(!mutex.WaitOne(0))return true;
            }catch(AbandonedMutexException) { }
            mutex.ReleaseMutex();return false;
        }
    }
    internal static bool Ready(string prefix=null) {
        prefix=prefix??Prefix;
        EventWaitHandle ready;
        if(!EventWaitHandle.TryOpenExisting(prefix+"Ready",out ready))return false;
        using(ready)return ready.WaitOne(0);
    }
    internal static void Stop(string prefix=null) {
        prefix=prefix??Prefix;
        EventWaitHandle stop;
        if(EventWaitHandle.TryOpenExisting(prefix+"Stop",out stop))using(stop)stop.Set();
    }
}
