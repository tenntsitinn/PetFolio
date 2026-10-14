using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class PetTrackingTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static void Invoke(PetStateService service, string method)
    {
        typeof(PetStateService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, null);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Run();
            Console.WriteLine(checks + " pet tracking and diagnostic checks passed");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        string root = Path.Combine(Program.DataFolder, "tracking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "config.toml"), state = Path.Combine(root, ".codex-global-state.json");
        var json = new JavaScriptSerializer();
        int candidates = 1;
        uint fixtureDpi = 120;
        var callbacks = new ConcurrentQueue<Action>();
        int dispatched = 0;
        Action pump = () =>
        {
            Action callback;
            while (callbacks.TryDequeue(out callback))
                callback();
            Application.DoEvents();
        };
        Action<bool, int> write = (open, x) =>
        {
            File.WriteAllText(state, json.Serialize(new Dictionary<string, object> {
                { "electron-avatar-overlay-open", open }, { "electron-avatar-overlay-bounds", new { x = x, y = 400 } } }));
            File.SetLastWriteTimeUtc(state, DateTime.UtcNow.AddTicks(x));
        };
        File.WriteAllText(config, "[desktop]\nselected-avatar-id = \"\"\navatar-overlay-mascot-width-px = 112\n");
        write(true, 600);
        try
        {
            using (var target = new Form())
            using (var pets = new PetStateService(root, Path.Combine(root, "palettes.json"), a =>
            {
                Interlocked.Increment(ref dispatched);
                callbacks.Enqueue(a);
            },
                () => candidates == 0 ? new List<IntPtr>() : candidates == 1 ? new List<IntPtr> { target.Handle } : new List<IntPtr> { target.Handle, target.Handle },
                hwnd => fixtureDpi))
            {
                target.ShowInTaskbar = false;
                target.Bounds = new Rectangle(600, 400, 112, 122);
                target.Show();
                int published = 0, diagnostics = 0;
                pets.Changed += s => published++;
                pets.DiagnosticChanged += d => diagnostics++;
                pets.Start();
                Check(pets.Current.Visible && pets.Diagnostic.Reason == "ready", "Known fixture window binds with readable desktop state");
                Check(pets.Current.Anchor == new Point(750, 500) && pets.Current.Size == 140, "Startup uses the discovered window DPI before publishing");
                fixtureDpi = 96;
                Invoke(pets, "Tick");
                long dpiStateReads = pets.StateReads;
                fixtureDpi = 120;
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor == new Point(750, 500) && pets.Current.Size == 140, "Polling observes a DPI change without a state file write");
                Check(pets.Current.Bounds.Contains(new Point(880, 640)), "Scaled hit bounds contain a pointer outside the old unscaled bounds");
                Check(pets.StateReads == dpiStateReads, "DPI updates reuse original DIP metadata without parsing the file again");
                fixtureDpi = 144;
                pets.RefreshDisplayMetrics();
                pump();
                Check(pets.Current.Anchor == new Point(900, 600) && pets.Current.Size == 168, "Display notification refreshes position and size");
                fixtureDpi = 0;
                Invoke(pets, "Tick");
                Check(pets.Current.Size == 168, "Transient DPI query failure preserves the last valid scale");
                fixtureDpi = 96;
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor == new Point(600, 400) && pets.Current.Size == 112, "Returning to 100% restores original DIP values");
                long reads = pets.SelectionReads, stateReads = pets.StateReads;
                int before = published;
                for (int i = 0; i < 100; i++)
                {
                    Invoke(pets, "ReadSelection");
                    Invoke(pets, "ReadAnchor");
                    Invoke(pets, "Tick");
                }

                Check(pets.SelectionReads == reads && pets.StateReads == stateReads, "Unchanged configuration and state never repeat parsing");
                Check(published == before && diagnostics == 1, "Stationary pet avoids duplicate snapshots and diagnostics");
                File.WriteAllText(config, "[desktop]\nselected-avatar-id = \"\"\navatar-overlay-mascot-width-px = 160\n");
                File.SetLastWriteTimeUtc(config, DateTime.UtcNow.AddSeconds(1));
                Invoke(pets, "ReadSelection");
                Invoke(pets, "Tick");
                Check(pets.Current.Size == 160 && published == before + 1, "Size changes are observed even when saved bounds do not change");
                write(true, 649);
                typeof(PetStateService).GetField("nextMetadata", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pets, Int64.MaxValue);
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor.X == 649, "Idle timer callbacks observe changed bounds without a second metadata deadline");
                File.WriteAllText(config, "[desktop]\nselected-avatar-id = \"A\"\nselected-avatar-id = \"unfinished");
                Invoke(pets, "ReadSelection");
                Invoke(pets, "Tick");
                Check(pets.Current.PetId == "", "Failed parsing cannot publish a partially read selection");
                write(true, 650);
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor.X == 650, "Changed saved position still moves the tracked anchor");
                File.WriteAllText(state, "{\"electron-avatar-overlay-open\":true,\"electron-avatar-overlay-bounds\":{\"x\":\"bad\",\"y\":400}}");
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "state-read-failed", "Invalid coordinate data produces a diagnostic instead of escaping the UI poll");
                write(true, 651);
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(pets.Current.Visible && pets.Diagnostic.Reason == "ready", "Valid state recovers after a malformed update");
                File.WriteAllText(state, "{}");
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(!pets.Current.Visible && pets.Diagnostic.Reason == "anchor-unavailable", "Removed position fields cannot leave a stale bound bubble");
                write(false, 652);
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "pet-closed", "A closed Pet has an explicit reason");
                File.Delete(state);
                Invoke(pets, "ReadAnchor");
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "state-file-missing", "Missing desktop state has an explicit reason");
                write(true, 653);
                Invoke(pets, "ReadAnchor");
                target.Hide();
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "window-hidden" && !pets.Current.Visible, "Hidden matching window is distinguished from failed discovery");
                target.Show();
                candidates = 0;
                typeof(PetStateService).GetField("nextDiscovery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pets, 0L);
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "window-not-found", "No matching windows yields a specific diagnostic");
                candidates = 2;
                typeof(PetStateService).GetField("nextDiscovery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pets, 0L);
                Invoke(pets, "Tick");
                Check(pets.Diagnostic.Reason == "ambiguous-windows" && pets.Current.Target == IntPtr.Zero, "Ambiguous discovery never binds an arbitrary window");
                var report = json.Serialize(PetDiagnostics.Capture(pets.Diagnostic, IntPtr.Zero));
                Check(report.Contains("candidateWindows") && report.Contains("displays") && report.Contains("scale") && report.Contains("windows"), "Diagnostics include discovery, OS and monitor setup");
                Check(!report.Contains(root) && !report.Contains("selected-avatar-id"), "Reports omit local paths and unrelated configuration");
                Check(PetDiagnostics.WindowsVersion.Contains("build"), "Windows diagnostic reads the real build instead of manifest compatibility version");
                candidates = 1;
                typeof(PetStateService).GetField("nextDiscovery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pets, 0L);
                write(true, 700);
                Invoke(pets, "Tick");
                var timer = (System.Windows.Forms.Timer)typeof(PetStateService).GetField("follow", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pets);
                timer.Stop();
                write(true, 701);
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (pets.Current.Anchor.X != 701 && deadline.ElapsedMilliseconds < 1500)
                {
                    pump();
                    Thread.Sleep(1);
                }

                Check(pets.Current.Anchor.X == 701, "File notifications publish new position with polling stopped");
                string replacement = Path.Combine(root, "replacement.json");
                File.WriteAllText(replacement, json.Serialize(new { electron = new Dictionary<string, object> { { "electron-avatar-overlay-open", true }, { "electron-avatar-overlay-bounds", new { x = 702, y = 400 } } } }));
                File.Replace(replacement, state, null);
                deadline.Restart();
                while (pets.Current.Anchor.X != 702 && deadline.ElapsedMilliseconds < 1500)
                {
                    pump();
                    Thread.Sleep(1);
                }

                Check(pets.Current.Anchor.X == 702, "Atomic file replacement is observed through rename notifications");
                File.WriteAllText(config, "[desktop]\nselected-avatar-id = \"\"\navatar-overlay-mascot-width-px = 180\n");
                deadline.Restart();
                while (pets.Current.Size != 180 && deadline.ElapsedMilliseconds < 1500)
                {
                    pump();
                    Thread.Sleep(1);
                }

                Check(pets.Current.Size == 180, "Config notifications update size without a polling callback");
                fixtureDpi = 120;
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor == new Point(878, 500) && pets.Current.Size == 225, "Changed metadata is scaled from its original DIP values");
                fixtureDpi = 144;
                Invoke(pets, "Tick");
                Check(pets.Current.Anchor == new Point(1053, 600) && pets.Current.Size == 270, "Repeated DPI changes do not accumulate rounded position error");
                fixtureDpi = 96;
                Invoke(pets, "Tick");
                deadline.Restart();
                while (deadline.ElapsedMilliseconds < 100)
                {
                    pump();
                    Thread.Sleep(1);
                }

                int dispatchedBefore = dispatched, publishedBefore = published;
                long parsedBefore = pets.StateReads;
                var queue = typeof(PetStateService).GetMethod("QueueMetadata", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < 20; i++)
                    queue.Invoke(pets, new object[] { 2 });
                Check(dispatched == dispatchedBefore + 1, "A notification burst dispatches only one UI callback");
                deadline.Restart();
                while (deadline.ElapsedMilliseconds < 60)
                {
                    pump();
                    Thread.Sleep(1);
                }

                Check(pets.StateReads == parsedBefore + 1 && published == publishedBefore, "Duplicate notifications coalesce into one parse without duplicate snapshots");
                queue.Invoke(pets, new object[] { 2 });
                int stopped = published;
                pets.Dispose();
                Invoke(pets, "Tick");
                Check(published == stopped, "Disposed tracking cannot publish more state");
                write(true, 703);
                deadline.Restart();
                while (deadline.ElapsedMilliseconds < 60)
                {
                    pump();
                    Thread.Sleep(1);
                }

                Check(published == stopped, "Queued notifications after disposal cannot publish state");
            }

            string selected;
            int? size;
            File.WriteAllText(config, "[desktop]\nselected-avatar-id = 'A' # comment\navatar-overlay-mascot-width-px = 300 # comment\n");
            Check(PetSelectionConfig.TryRead(config, out selected, out size) && selected == "A" && size == 224, "One config pass reads selection and validates size");
            File.WriteAllText(config, "[desktop]\nselected-avatar-id = \"unfinished");
            Check(!PetSelectionConfig.TryRead(config, out selected, out size), "Partial selection writes remain retryable");
            Check(PetDiagnostics.PackageVersion(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.1009.12345.0_x64__sample\app\ChatGPT.exe") == "26.1009.12345.0", "Codex version comes from its package identity instead of the Electron executable version");
            Check(PetDiagnostics.PackageVersion(@"C:\unknown\ChatGPT.exe") == null, "An unknown installation does not invent a Codex version");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(root))
                File.Delete(file);
            Directory.Delete(root);
        }
    }
}
