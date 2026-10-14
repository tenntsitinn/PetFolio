using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Explicit desktop-input opt-in. All windows, config and quota belong to fixtures.
static class PairedPerformanceProbe
{
    sealed class PetWindow : Form
    {
        bool dragging;
        Point pressed, seed;
        internal int Presses;
        internal Action<Point> Released;
        protected override bool ShowWithoutActivation
        {
            get
            {
                return true;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21)
            {
                m.Result = new IntPtr(3);
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            dragging = true;
            pressed = Cursor.Position;
            seed = Location;
            Capture = true;
            Presses++;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                var p = Cursor.Position;
                Location = new Point(seed.X + p.X - pressed.X, seed.Y + p.Y - pressed.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !dragging)
                return;
            dragging = false;
            Capture = false;
            if (Released != null)
                Released(Location);
        }
    }

    sealed class Quota : IQuotaSource
    {
        public QuotaSnapshot Read()
        {
            return new QuotaSnapshot(DateTime.UtcNow, new QuotaWindow(20, 300, null), new QuotaWindow(30, 10080, null));
        }

        public void Dispose()
        {
        }
    }

    sealed class MotionTrace
    {
        readonly object gate = new object ();
        readonly Stopwatch clock;
        Point expected;
        double at, previous;
        bool measuring;
        long sequence, lastObserved;
        internal int Inputs, DraggingUpdates;
        internal readonly List<double> Latency = new List<double>(), Intervals = new List<double>();
        internal MotionTrace(Stopwatch clock)
        {
            this.clock = clock;
        }

        internal void Input(Point point)
        {
            lock (gate)
            {
                if (point == expected)
                    return;
                expected = point;
                at = clock.Elapsed.TotalMilliseconds;
                sequence++;
                if (measuring)
                    Inputs++;
            }
        }

        internal void Observe(PetState state, QuotaLabel view)
        {
            lock (gate)
            {
                if (!measuring || !state.Visible || state.Anchor != expected || sequence == lastObserved)
                    return;
                Native.RECT rectangle;
                if (!Native.GetWindowRect(view.Handle, out rectangle))
                    return;
                // Fixture is kept far from edges with top-end placement throughout.
                if (rectangle.Left != state.Anchor.X + 124 || rectangle.Top != state.Anchor.Y)
                    return;
                lastObserved = sequence;
                double now = clock.Elapsed.TotalMilliseconds;
                Latency.Add(now - at);
                if (previous > 0)
                    Intervals.Add(now - previous);
                previous = now;
                if (state.Dragging)
                    DraggingUpdates++;
            }
        }

        internal void Begin()
        {
            lock (gate)
            {
                Inputs = DraggingUpdates = 0;
                Latency.Clear();
                Intervals.Clear();
                previous = 0;
                lastObserved = sequence;
                measuring = true;
            }
        }

        internal void End()
        {
            lock (gate)
            {
                measuring = false;
            }
        }
    }

    static readonly JavaScriptSerializer json = new JavaScriptSerializer();
    static string cancelFile;
    static void Pump(double seconds)
    {
        var clock = Stopwatch.StartNew();
        long nextCancelCheck = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (clock.ElapsedMilliseconds >= nextCancelCheck)
            {
                nextCancelCheck = clock.ElapsedMilliseconds + 250;
                if (File.Exists(cancelFile))
                    throw new OperationCanceledException("Benchmark cancelled.");
            }

            Application.DoEvents();
            int remaining = (int)((seconds - clock.Elapsed.TotalSeconds) * 1000);
            if (remaining > 0)
                MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, (uint)Math.Min(200, remaining), 0x4ff, 4);
        }
    }

    static double? Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
            return null;
        var copy = values.ToArray();
        Array.Sort(copy);
        double rank = (copy.Length - 1) * p;
        int i = (int)rank;
        return copy[i] + (copy[Math.Min(i + 1, copy.Length - 1)] - copy[i]) * (rank - i);
    }

    static Point Position(Point origin, double seconds)
    {
        return new Point(origin.X + (int)(80 * Math.Sin(seconds * 2)), origin.Y + (int)(25 * Math.Cos(seconds * 2)));
    }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run(string[] args)
    {
        if (args.Length != 6 && args.Length != 7)
            throw new ArgumentException("output seconds warmup label native-input cancel-file [scenarios]");
        double seconds = Double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), warmup = Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
        bool native = args[4] == "true";
        cancelFile = args[5];
        var selected = new HashSet<string>(args.Length == 7 ? args[6].Split(',') : new[] { "idle-quota", "moving-quota", "native-drag", "system-low-opacity", "hidden-bubble", "pet-closed" });
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PETFOLIO_DATA_DIR", root);
        string stateFile = Path.Combine(root, ".codex-global-state.json");
        string configFile = Path.Combine(root, "config.toml"), paletteFile = Path.Combine(root, "palette.json");
        File.WriteAllText(configFile, "appearanceTheme = \"dark\"\n[desktop]\nselected-avatar-id = \"benchmark\"\n");
        var sourceInfo = new FileInfo(configFile);
        File.WriteAllText(paletteFile, json.Serialize(new { entries = new { benchmark = new { r = 218, g = 165, b = 32,
            sourcePath = configFile, sourceBytes = sourceInfo.Length, lastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks.ToString() } } }));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var results = new List<object>();
        var timeline = Stopwatch.StartNew();
        var trace = new MotionTrace(timeline);
        Point originalCursor = Cursor.Position;
        bool pressed = false;
        Thread input = null;
        var stopInput = new ManualResetEvent(false);
        try
        {
            using (var target = new PetWindow())
            using (var dispatcher = new Control())
            using (var motion = new System.Windows.Forms.Timer())
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                var origin = new Point(area.Left + area.Width / 2 - 100, area.Top + area.Height / 2 - 50);
                target.FormBorderStyle = FormBorderStyle.None;
                target.ShowInTaskbar = false;
                target.TopMost = true;
                target.BackColor = Color.Goldenrod;
                target.Bounds = new Rectangle(origin, new Size(112, 122));
                target.Show();
                IntPtr petHandle = target.Handle;
                var dispatchHandle = dispatcher.Handle;
                bool missing = false;
                Action<Point, bool> write = (point, open) => File.WriteAllText(stateFile, json.Serialize(new Dictionary<string, object> {
                    { "electron-avatar-overlay-open", open }, { "electron-avatar-overlay-bounds", new { x = point.X, y = point.Y } } }));
                target.Released = point => write(point, true);
                write(origin, true);
                using (var pets = new PetStateService(root, paletteFile, a => dispatcher.BeginInvoke(a),
                    () => missing ? new List<IntPtr>() : new List<IntPtr> { petHandle }))
                using (var feature = new QuotaFeature(root, pets, () => new Quota(), a => dispatcher.BeginInvoke(a), s =>
                {
                }, Program.Record))
                {
                    pets.Start();
                    feature.Start();
                    feature.View.ReadHoverPointer = () => Point.Empty;
                    pets.Changed += state => trace.Observe(state, feature.View);
                    motion.Interval = 100;
                    var motionClock = new Stopwatch();
                    motion.Tick += (s, e) =>
                    {
                        var point = Position(origin, motionClock.Elapsed.TotalSeconds);
                        trace.Input(point);
                        target.Location = point;
                        write(point, true);
                    };
                    Action<string> measure = name =>
                    {
                        if (!selected.Contains(name))
                            return;
                        Pump(warmup);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        using (var process = Process.GetCurrentProcess())
                        {
                            ulong idle, kernel, user;
                            GetSystemTimes(out idle, out kernel, out user);
                            var cpu = process.TotalProcessorTime;
                            long selections = pets.SelectionReads, states = pets.StateReads, publications = pets.Publications;
                            long frames = feature.View.FramesRendered, backgrounds = feature.View.BackgroundReads;
                            int collections = GC.CollectionCount(0);
                            trace.Begin();
                            var clock = Stopwatch.StartNew();
                            Pump(seconds);
                            double elapsed = clock.Elapsed.TotalSeconds;
                            trace.End();
                            process.Refresh();
                            ulong idleEnd, kernelEnd, userEnd;
                            GetSystemTimes(out idleEnd, out kernelEnd, out userEnd);
                            bool valid = name != "native-drag" || (target.Presses > 0 && trace.DraggingUpdates >= seconds * 5);
                            if (name == "moving-quota")
                                valid = trace.Inputs > 0 && trace.Latency.Count >= trace.Inputs * .8;
                            double cpuPercent = 100 * (process.TotalProcessorTime - cpu).TotalSeconds / elapsed;
                            results.Add(new { scenario = name, valid = valid, seconds = elapsed, cpuCorePercent = cpuPercent,
                                hostCpuPercent = SystemMetrics.Cpu(idleEnd, kernelEnd, userEnd, idle, kernel, user),
                                workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = process.PrivateMemorySize64 / 1048576.0, managedKiB = GC.GetTotalMemory(false) / 1024.0,
                                gen0Collections = GC.CollectionCount(0) - collections, selectionReads = pets.SelectionReads - selections, stateReads = pets.StateReads - states,
                                publications = pets.Publications - publications, frames = feature.View.FramesRendered - frames, backgroundReads = feature.View.BackgroundReads - backgrounds,
                                systemSampling = feature.SystemService.IsRunning, motionInputs = trace.Inputs, observedMotionUpdates = trace.Latency.Count,
                                draggingUpdates = trace.DraggingUpdates, latencyMedianMs = Percentile(trace.Latency, .5), latencyP95Ms = Percentile(trace.Latency, .95),
                                updateIntervalMedianMs = Percentile(trace.Intervals, .5), updateIntervalP95Ms = Percentile(trace.Intervals, .95) });
                            File.WriteAllText(args[0], json.Serialize(new { label = args[3], complete = false, windows = PetDiagnostics.WindowsVersion, warmupSeconds = warmup, results = results }));
                            Console.WriteLine(args[3] + " " + name + ": CPU=" + cpuPercent.ToString("0.00") + "%, valid=" + valid);
                            if (!valid)
                                throw new InvalidOperationException("Motion was not sufficiently observed at the bubble window; results are invalid.");
                        }
                    };
                    measure("idle-quota");
                    motionClock.Start();
                    motion.Start();
                    measure("moving-quota");
                    motion.Stop();
                    write(origin, true);
                    target.Location = origin;
                    if (native)
                    {
                        Pump(.5);
                        if ((Native.GetAsyncKeyState(1) & 0x8000) != 0)
                            throw new InvalidOperationException("Mouse button already held.");
                        var start = new Point(origin.X + 56, origin.Y + 61);
                        SetCursorPos(start.X, start.Y);
                        Pump(.2);
                        if (Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = start.X, Y = start.Y }), 2) != petHandle)
                            throw new InvalidOperationException("Fixture pet is covered; native input refused.");
                        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                        pressed = true;
                        Pump(.2);
                        input = new Thread(() =>
                        {
                            var clock = Stopwatch.StartNew();
                            double next = 0;
                            while (!stopInput.WaitOne(0))
                            {
                                var point = Position(origin, clock.Elapsed.TotalSeconds);
                                trace.Input(point);
                                SetCursorPos(start.X + point.X - origin.X, start.Y + point.Y - origin.Y);
                                next += .02;
                                int wait = Math.Max(1, (int)((next - clock.Elapsed.TotalSeconds) * 1000));
                                stopInput.WaitOne(wait);
                            }
                        });
                        input.IsBackground = true;
                        input.Start();
                        try
                        {
                            measure("native-drag");
                        }
                        finally
                        {
                            stopInput.Set();
                            input.Join(3000);
                            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                            pressed = false;
                            Pump(.3);
                            SetCursorPos(originalCursor.X, originalCursor.Y);
                        }

                        target.Location = origin;
                        write(origin, true);
                    }

                    feature.View.SetDefaultAppearance(false);
                    typeof(QuotaLabel).GetMethod("SetOpacity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(feature.View, new object[] { 20 });
                    feature.View.SetMode(true);
                    measure("system-low-opacity");
                    feature.View.SetBubbleVisible(false);
                    measure("hidden-bubble");
                    missing = true;
                    write(origin, false);
                    measure("pet-closed");
                }
            }

            File.WriteAllText(args[0], json.Serialize(new { label = args[3], complete = true, at = DateTime.UtcNow.ToString("o"), windows = PetDiagnostics.WindowsVersion,
                logicalProcessors = Environment.ProcessorCount, warmupSeconds = warmup, measurementSeconds = seconds, nativeInput = native, results = results }));
        }
        finally
        {
            stopInput.Set();
            if (input != null)
                input.Join(3000);
            if (pressed)
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            if (native)
                SetCursorPos(originalCursor.X, originalCursor.Y);
            stopInput.Dispose();
            foreach (var file in Directory.GetFiles(root))
                File.Delete(file);
            Directory.Delete(root);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("user32.dll")]
    static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);
}
