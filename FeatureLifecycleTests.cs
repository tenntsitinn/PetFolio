using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

static class FeatureLifecycleTests
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        checks++;
    }

    sealed class Pets : IPetStateSource
    {
        Action<PetState> changed;
        internal PetState State = PetState.Empty;
        public PetState Current
        {
            get
            {
                return State;
            }
        }

        public int Subscribers
        {
            get
            {
                return changed == null ? 0 : changed.GetInvocationList().Length;
            }
        }

        public event Action<PetState> Changed
        {
            add
            {
                changed += value;
            }

            remove
            {
                changed -= value;
            }
        }

        public void Tick()
        {
            if (changed != null)
                changed(Current);
        }
    }

    sealed class Source : IQuotaSource
    {
        public int Disposals, Calls;
        public QuotaSnapshot Read()
        {
            Interlocked.Increment(ref Calls);
            return new QuotaSnapshot(DateTime.UtcNow, new QuotaWindow(20, 300, null), null);
        }

        public void Dispose()
        {
            Disposals++;
        }
    }

    sealed class Counters : ISystemMetricsSource
    {
        internal int Reads;
        public SystemMetrics Read()
        {
            Reads++;
            return new SystemMetrics
            {
                CpuPercent = Reads == 1 ? (double? )null : 25,
                MemoryPercent = 50
            };
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var pets = new Pets();
        var sources = new List<Source>();
        var callbacks = new ConcurrentQueue<Action>();
        using (var posted = new AutoResetEvent(false))
        using (var feature = new QuotaFeature("", pets, () =>
        {
            var s = new Source();
            sources.Add(s);
            return s;
        },
            action =>
        {
            callbacks.Enqueue(action);
            posted.Set();
        }, s =>
        {
        }, (k, d) =>
        {
        }))
        {
            Check(!feature.IsRunning && pets.Subscribers == 0, "Construction owns no live feature resources");
            feature.Start();
            feature.Start();
            Check(posted.WaitOne(5000), "Quota request completes without depending on a visible view");
            Check(sources.Count == 1 && pets.Subscribers == 1 && feature.Commands.Length == 4, "Start is idempotent and registers one subscriber");
            var first = feature.View;
            var firstButton = first.DismissButton;
            bool glassAvailable = first.Owner is GlassBackdrop && ((GlassBackdrop)first.Owner).Ready;
            feature.View.SetBubbleVisible(false);
            pets.Tick();
            Check(feature.IsRunning && sources[0].Disposals == 0 && !feature.View.BubbleRequested, "Hiding the bubble preserves the running feature");
            feature.Restore();
            Check(feature.View == first && feature.View.BubbleRequested, "Restore reuses the live view");
            feature.Stop();
            feature.Stop();
            Check(!feature.IsRunning && first.IsDisposed && firstButton.IsDisposed, "Stop disposes the view and child surfaces");
            Check(pets.Subscribers == 0 && sources[0].Disposals == 1 && feature.Commands.Length == 0, "Stop releases query source, subscription and commands");
            feature.Start();
            Check(posted.WaitOne(5000), "Restart launches a fresh request");
            Check(sources.Count == 2 && pets.Subscribers == 1 && feature.View != first, "Restart creates a fresh feature instance without duplicating subscribers");
            Check(!glassAvailable || (feature.View.Owner is GlassBackdrop && ((GlassBackdrop)feature.View.Owner).Ready), "Restart preserves available glass instead of silently falling back");
            Action callback;
            while (callbacks.TryDequeue(out callback))
                callback();
            Check(!feature.View.IsDisposed, "Old queued completions are harmless after restart");
            for (int i = 0; i < 3; i++)
            {
                feature.Stop();
                feature.Start();
                Check(posted.WaitOne(5000), "Repeated restart completes");
                while (callbacks.TryDequeue(out callback))
                    callback();
                Check(pets.Subscribers == 1, "Repeated restarts retain exactly one pet subscription");
                Check(!glassAvailable || (feature.View.Owner is GlassBackdrop && ((GlassBackdrop)feature.View.Owner).Ready), "Every restart preserves available glass");
            }

            using (var sibling = new QuotaFeature("", pets, () => new Source(), action =>
            {
            }, s =>
            {
            }, (k, d) =>
            {
            }))
            {
                sibling.Start();
                Check(pets.Subscribers == 2, "Two features share one pet state source");
                Check(!glassAvailable || (sibling.View.Owner is GlassBackdrop && ((GlassBackdrop)sibling.View.Owner).Ready), "Concurrent feature surfaces share the composition dispatcher");
                feature.Stop();
                pets.Tick();
                Check(sibling.IsRunning && pets.Subscribers == 1, "Stopping one feature leaves its sibling running");
            }

            feature.Dispose();
            Check(pets.Subscribers == 0 && !feature.IsRunning, "Final disposal releases the feature");
            foreach (var source in sources)
                Check(source.Disposals == 1, "Each source is disposed exactly once");
            bool rejected = false;
            try
            {
                feature.Start();
            }
            catch (ObjectDisposedException)
            {
                rejected = true;
            }

            Check(rejected, "Final disposal cannot be restarted");
        }

        SystemLifecycle();
        Console.WriteLine(checks + " feature lifecycle checks passed");
    }

    static void SystemLifecycle()
    {
        string path = Path.Combine(Program.DataFolder, "lifecycle-settings-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new BubbleSettingsStore(path, (k, d) =>
        {
        });
        var pets = new Pets();
        var visible = new PetState(new IntPtr(1), new Point(600, 400), 112, true, true, false, "test", Color.Gold);
        pets.State = visible;
        var counters = new List<Counters>();
        var quotas = new List<Source>();
        try
        {
            using (var feature = new QuotaFeature("", pets, () =>
            {
                var source = new Source();
                quotas.Add(source);
                return source;
            }, a =>
            {
            }, s =>
            {
            }, (k, d) =>
            {
            }, store,
                () =>
            {
                var source = new Counters();
                counters.Add(source);
                return source;
            }))
            {
                feature.Start();
                var view = feature.View;
                var system = feature.SystemService;
                Check(view.Visible && !system.IsRunning && counters.Count == 0, "Quota mode creates no system counter source");
                view.SetMode(true);
                Check(system.IsRunning && counters.Count == 1 && counters[0].Reads == 1, "Mode request starts system sampling through the feature");
                Check(quotas.Count == 1 && quotas[0].Disposals == 0, "System display preserves enabled quota polling");
                pets.State = PetState.Empty;
                pets.Tick();
                Check(!view.Visible && !system.IsRunning, "Pet disappearance stops system sampling");
                pets.State = visible;
                pets.Tick();
                Check(view.Visible && system.IsRunning && counters.Count == 2, "Pet restoration starts a new counter baseline");
                view.SetBubbleVisible(false);
                Check(!system.IsRunning, "Explicit hiding stops the feature-owned sampler");
                view.RefreshCurrent();
                Check(counters.Count == 3 && counters[2].Reads == 1 && !system.IsRunning, "Hidden system refresh stays a one-shot sample");
                feature.Restore();
                Check(feature.View == view && counters.Count == 4 && system.IsRunning, "Restore reuses the view and resets the system baseline");
                var quotaOption = (ToolStripMenuItem)feature.FeatureOptions[0];
                var systemOption = (ToolStripMenuItem)feature.FeatureOptions[1];
                quotaOption.PerformClick();
                Check(quotas[0].Disposals == 1 && system.IsRunning, "Disabling quota releases only its data service");
                systemOption.PerformClick();
                Check(!view.Visible && !system.IsRunning && !view.AnyModeEnabled, "Disabling both modes hides and stops sampling");
                feature.Restore();
                Check(!view.Visible && !system.IsRunning, "Restore cannot override disabled content");
                var saved = store.Load();
                Check(!saved.QuotaEnabled && !saved.SystemEnabled && saved.SystemMode, "Feature persists authoritative switches and mode");
                systemOption.PerformClick();
                Check(view.Visible && system.IsRunning && counters.Count == 5, "Re-enabling System restores the requested bubble with fresh counters");
                feature.Stop();
                Check(!system.IsRunning && view.IsDisposed && pets.Subscribers == 0, "Stop releases system service and view subscriptions");
                int before = counters.Count;
                system.Refresh();
                system.SetRunning(true);
                Check(counters.Count == before, "An old disposed sampler cannot produce work after stop");
                feature.Start();
                Check(feature.View.SystemMode && feature.SystemService.IsRunning && counters.Count == before + 1, "Restart restores persisted mode and starts a fresh sampler");
                Check(feature.SystemService != system && quotas.Count == 1, "Restart creates a new sampler while disabled quota remains stopped");
                ((ToolStripMenuItem)feature.FeatureOptions[0]).PerformClick();
                Check(quotas.Count == 2 && feature.View.SystemMode, "Re-enabling quota creates its service without switching the view");
                ((ToolStripMenuItem)feature.FeatureOptions[1]).PerformClick();
                Check(!feature.View.SystemMode && !feature.SystemService.IsRunning, "Disabling active System falls back to quota and stops counters");
            }

            Check(quotas[1].Disposals == 1 && pets.Subscribers == 0, "Final disposal releases the resumed quota service");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
