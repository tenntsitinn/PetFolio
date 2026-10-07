using System;
using System.Threading.Tasks;
using System.Windows.Forms;

// UI-thread lifecycle; the source runs in the background. Dispatch is supplied by
// the host, never by a feature window that might be hidden or destroyed.
sealed class QuotaService : IDisposable {
    readonly IQuotaSource source;
    readonly Action<Action> dispatch;
    readonly Action<QuotaSnapshot> persist;
    readonly Action<string, object> record;
    readonly Timer refresh = new Timer();
    bool busy, started;
    volatile bool disposed;
    public QuotaState State { get; private set; }
    public event Action<QuotaState> Changed;
    public QuotaService(IQuotaSource source, Action<Action> dispatch,
        Action<QuotaSnapshot> persist, Action<string, object> record) {
        this.source = source; this.dispatch = dispatch; this.persist = persist; this.record = record;
        State = new QuotaState(null, null);
        refresh.Interval = 300000;
        refresh.Tick += (s, e) => Refresh();
    }
    public void Start() {
        if (disposed) throw new ObjectDisposedException("QuotaService");
        if (started) return;
        started = true; refresh.Start(); Refresh();
    }
    public void Refresh() {
        if (disposed || busy) return;
        busy = true;
        State = new QuotaState(State.Snapshot, State.Error, true);
        var updating = Changed; if (updating != null) updating(State);
        if (disposed) return;
        Task.Factory.StartNew(() => {
            QuotaSnapshot snapshot = null; string error = null;
            try { snapshot = source.Read(); if (snapshot == null) throw new InvalidOperationException("Empty quota response"); }
            catch (Exception ex) { error = ex.Message; }
            if (disposed) return;
            dispatch(() => {
                if (disposed) return;
                busy = false;
                State = new QuotaState(snapshot ?? State.Snapshot, error);
                if (error != null) record("quota-error", new { error = error });
                else {
                    try { persist(snapshot); }
                    catch (Exception ex) { record("quota-snapshot-error", new { error = ex.GetType().Name }); }
                    record("quota-read", snapshot.ToRecord());
                }
                var handler = Changed; if (handler != null) handler(State);
            });
        });
    }
    public void Dispose() {
        if (disposed) return;
        disposed = true; refresh.Stop(); refresh.Dispose();
        Changed = null; source.Dispose();
    }
}
