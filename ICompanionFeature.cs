using System;
using System.Windows.Forms;

// In-process features share host services; stopping one must not stop the host.
interface ICompanionFeature : IDisposable {
    string Name { get; }
    bool IsRunning { get; }
    ToolStripItem[] Commands { get; }
    void Start();
    void Stop();
    void Restore();
}
