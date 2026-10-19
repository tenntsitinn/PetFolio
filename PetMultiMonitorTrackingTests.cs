using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class PetMultiMonitorTrackingTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static object Field(PetStateService service, string name)
    {
        return typeof(PetStateService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
    }

    static void Invoke(PetStateService service, string name, params object[] args)
    {
        typeof(PetStateService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, args);
    }

    static void Refresh(PetStateService service)
    {
        typeof(PetStateService).GetField("nextDisplayRead", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, 0L);
        Invoke(service, "UpdatePhysicalCoordinates", new object[] { null });
    }

    static void Publish(PetStateService service)
    {
        Invoke(service, "ProjectAnchor");
        Invoke(service, "Publish", true);
    }

    [STAThread]
    static void Main()
    {
        try
        {
            Run();
            Console.WriteLine(checks + " multi-monitor tracking checks passed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.InnerException == null ? ex.Message : ex.InnerException.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        var root = Path.Combine(Program.DataFolder, "multi-monitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string state = Path.Combine(root, ".codex-global-state.json");
        var primary = new PetMonitor("main", new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), 96, true);
        var right = new PetMonitor("right", new Rectangle(1920, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1040), 144, false);
        var snapshot = new PetDesktopSnapshot(new[] { primary, right });
        int reads = 0, writes = 0;
        bool failRead = false;
        var json = new JavaScriptSerializer();
        Action<int, int, Rectangle?> write = (x, y, display) =>
        {
            var bounds = new Dictionary<string, object>
            {
                {
                    "x",
                    x
                },
                {
                    "y",
                    y
                }
            };
            if (display.HasValue)
            {
                var b = display.Value;
                bounds.Add("displayBounds", new { x = b.X, y = b.Y, width = b.Width, height = b.Height });
            }

            File.WriteAllText(state, json.Serialize(new Dictionary<string, object> {
                { "electron-avatar-overlay-open", true }, { "electron-avatar-overlay-bounds", bounds } }));
            File.SetLastWriteTimeUtc(state, DateTime.UtcNow.AddSeconds(++writes));
        };
        File.WriteAllText(Path.Combine(root, "config.toml"), "[desktop]\navatar-overlay-mascot-width-px = 112\n");
        write(2020, 200, snapshot.Find("right").Logical.Bounds);
        try
        {
            using (var target = new Form())
            using (var service = new PetStateService(root, Path.Combine(root, "palettes.json"), a =>
            {
            },
                () => new List<IntPtr> { target.Handle }, hwnd => 96,
                (hwnd, dpi) =>
            {
                reads++;
                if (failRead)
                    throw new InvalidOperationException("Fixture unavailable");
                return snapshot;
            }))
            {
                target.ShowInTaskbar = false;
                target.Bounds = new Rectangle(300, 300, 112, 122);
                target.Show();
                service.Start();
                ((Timer)Field(service, "follow")).Stop();
                Check(service.Current.Visible && service.Current.Anchor == new Point(2070, 300), "Persisted right-screen DIP maps using both origins");
                Check(service.Current.Size == 168, "Published pet size uses mapped screen DPI, not foreign HWND DPI");
                int firstReads = reads;
                for (int i = 0; i < 20; i++)
                    Invoke(service, "UpdatePhysicalCoordinates", new object[] { null });
                Check(reads == firstReads, "Mouse/follow updates reuse the monitor snapshot");
                write(1700, 300, snapshot.Find("main").Logical.Bounds);
                Invoke(service, "ReadAnchor");
                Publish(service);
                Invoke(service, "BeginObservedDrag", new Point(1730, 345));
                var drag = (PetLogicalDrag)Field(service, "drag");
                Invoke(service, "MoveDrag", new Point(1954, 518));
                Publish(service);
                Check(service.Current.Anchor == new Point(1808, 300) && service.Current.Size == 112, "Physical pointer crossing still respects logical 24 DIP retention");
                Invoke(service, "MoveDrag", new Point(2070, 518));
                Publish(service);
                Check(service.Current.Anchor == new Point(2025, 450) && service.Current.Size == 168, "Full physical input to DIP drag to physical output chain");
                drag.End(((System.Diagnostics.Stopwatch)Field(service, "watch")).ElapsedMilliseconds);
                Publish(service);
                Check(service.Current.Anchor == new Point(2025, 450) && !service.Current.Dragging, "Pending release keeps destination screen despite old saved metadata");
                // Updating metrics during pending release must not rescale a
                // global pixel anchor or restart the release timeout.
                right = new PetMonitor("right", right.Pixels, right.WorkPixels, 192, false);
                snapshot = new PetDesktopSnapshot(new[] { primary, right });
                Refresh(service);
                Publish(service);
                Check(service.Current.Anchor == new Point(2060, 600) && service.Current.Size == 224, "Pending release reprojects its DIP anchor after DPI change");
                write(1991, 301, snapshot.Find("right").Logical.Bounds);
                Invoke(service, "ReadAnchor");
                Publish(service);
                Check(service.Current.Anchor == new Point(2062, 602), "Host saved update ends pending release in destination mapping");
                var left = new PetMonitor("left", new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1040), 144, false);
                snapshot = new PetDesktopSnapshot(new[] { primary, left });
                write(-1180, 100, snapshot.Find("left").Logical.Bounds);
                Invoke(service, "ReadAnchor");
                Refresh(service);
                Publish(service);
                Check(service.Current.Anchor == new Point(-1770, 150), "Negative physical anchor survives tracking publication");
                string hostBefore = File.ReadAllText(state);
                failRead = true;
                Refresh(service);
                Invoke(service, "Tick");
                Check(!service.Current.Visible && service.Diagnostic.Reason == "display-unavailable", "Unreliable mapping hides bubble with explicit diagnostic");
                int failedReads = reads;
                Invoke(service, "Tick");
                Check(reads == failedReads, "Failed monitor query is throttled");
                failRead = false;
                Refresh(service);
                Invoke(service, "Tick");
                Check(service.Current.Visible && service.Diagnostic.Reason == "ready", "Valid metrics restore following without touching host state");
                Check(File.ReadAllText(state) == hostBefore, "Following never writes or repairs the host position");
                // Legacy metadata without a display record is located in DIP.
                write(-1100, 200, null);
                Invoke(service, "ReadAnchor");
                Publish(service);
                Check(service.Current.Anchor == new Point(-1650, 300), "Legacy anchor without displayBounds uses logical display selection");
                write(-1100, 200, snapshot.Find("left").Logical.Bounds);
                Invoke(service, "ReadAnchor");
                snapshot = new PetDesktopSnapshot(new[] { primary });
                Refresh(service);
                Invoke(service, "Tick");
                Check(!service.Current.Visible && service.Diagnostic.Reason == "display-position-unavailable", "Removed saved display waits for host, without autonomous relocation");
                write(500, 300, primary.Pixels);
                Invoke(service, "ReadAnchor");
                Invoke(service, "Tick");
                Check(service.Current.Visible && service.Current.Anchor == new Point(500, 300), "Host-published location resumes following after unplug");
            }
        }
        finally
        {
            foreach (string file in Directory.GetFiles(root))
                File.Delete(file);
            Directory.Delete(root);
        }
    }
}
