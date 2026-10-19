using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Immutable snapshot shared by every feature; no feature owns pet discovery.
sealed class PetState
{
    public static readonly PetState Empty = new PetState(IntPtr.Zero, Point.Empty, 112, false, false, false, "", null);
    public IntPtr Target { get; private set; }
    public Point Anchor { get; private set; }
    public int Size { get; private set; }
    public bool AnchorKnown { get; private set; }
    public bool Visible { get; private set; }
    public bool Dragging { get; private set; }
    public string PetId { get; private set; }
    public Color? Colour { get; private set; }

    public Rectangle Bounds
    {
        get
        {
            return new Rectangle(Anchor.X, Anchor.Y, Size, (int)Math.Ceiling(Size * 208.0 / 192));
        }
    }

    public PetState(IntPtr target, Point anchor, int size, bool known, bool visible, bool dragging, string petId, Color? colour)
    {
        Target = target;
        Anchor = anchor;
        Size = size;
        AnchorKnown = known;
        Visible = visible;
        Dragging = dragging;
        PetId = petId;
        Colour = colour;
    }
}

interface IPetStateSource
{
    PetState Current { get; }

    event Action<PetState> Changed;
}

sealed class PetStateService : IPetStateSource, IDisposable
{
    readonly string home;
    readonly Action<Action> dispatch;
    readonly System.Windows.Forms.Timer follow = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer colourSample = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer metadataChanged = new System.Windows.Forms.Timer();
    readonly object notificationGate = new object ();
    FileSystemWatcher metadataWatcher;
    bool notificationQueued;
    int pendingMetadata;
    long notifiedSelectionReads, notifiedStateReads;
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly PetLogicalDrag drag = new PetLogicalDrag();
    readonly PetColourSwitch petSwitch = new PetColourSwitch();
    readonly PetPaletteCatalog palettes;
    readonly Func<List<IntPtr>> discoverWindows;
    readonly Func<IntPtr, uint> getWindowDpi;
    readonly Func<IntPtr, uint, PetDesktopSnapshot> readDesktop;
    PetDesktopSnapshot desktop;
    PetDesktopDisplay renderedDisplay;
    Rectangle? savedDisplayBounds;
    long nextDisplayRead;
    string displayError;
    internal long SelectionReads { get; private set; }
    internal long StateReads { get; private set; }
    internal long Publications { get; private set; }

    PetMouseObserver mouse;
    IntPtr target;
    DateTime fileTime, selectionFileTime;
    long selectionFileLength;
    bool selectionLoaded;
    int candidateCount;
    string stateError = "missing";
    // Persisted Electron values stay in DIP; all published and mouse values
    // are physical pixels. Always convert from the originals, never cumulatively.
    Point anchor, savedAnchor, savedAnchorDip;
    uint dpi = 96;
    bool displayRefreshQueued;
    long nextMetadata, nextDiscovery;
    int petSize = 112, petSizeDip = 112;
    bool anchorKnown, petOpen, colourBusy, dragStarted, dragReleased, started;
    volatile bool closing;
    Color? colour;
    public PetState Current { get; private set; }

    public event Action<PetState> Changed;
    internal PetTrackingDiagnostic Diagnostic { get; private set; }

    internal event Action<PetTrackingDiagnostic> DiagnosticChanged;
    public PetStateService(string home, string palettePath, Action<Action> dispatch, Func<List<IntPtr>> discoverWindows = null,
        Func<IntPtr, uint> getWindowDpi = null, Func<IntPtr, uint, PetDesktopSnapshot> readDesktop = null)
    {
        this.home = home;
        this.dispatch = dispatch;
        palettes = new PetPaletteCatalog(palettePath);
        this.discoverWindows = discoverWindows ?? DiscoverWindows;
        this.getWindowDpi = getWindowDpi ?? Native.GetDpiForWindow;
        this.readDesktop = readDesktop ?? WindowsPetDesktop.Read;
        Current = PetState.Empty;
        Diagnostic = new PetTrackingDiagnostic
        {
            Reason = "not-started"
        };
        follow.Interval = 16;
        follow.Tick += (s, e) => Tick();
        colourSample.Interval = 150;
        colourSample.Tick += (s, e) => SamplePetColour();
        metadataChanged.Interval = 16;
        metadataChanged.Tick += (s, e) =>
        {
            metadataChanged.Stop();
            int changed;
            bool selectionUnread, stateUnread;
            lock (notificationGate)
            {
                changed = pendingMetadata;
                selectionUnread = SelectionReads == notifiedSelectionReads;
                stateUnread = StateReads == notifiedStateReads;
                pendingMetadata = 0;
                notificationQueued = false;
            }

            if (closing)
                return;
            // A fallback poll may already have read this notification's write.
            if ((changed & 1) != 0 && selectionUnread)
                selectionLoaded = false;
            if ((changed & 2) != 0 && stateUnread)
                stateError = "changed";
            nextMetadata = 0;
            Tick();
        };
    }

    public void Start()
    {
        if (closing)
            throw new ObjectDisposedException("PetStateService");
        if (started)
            return;
        started = true;
        StartMetadataWatcher();
        try
        {
            mouse = new PetMouseObserver(MouseChanged);
        }
        catch (Exception ex)
        {
            Program.Record("drag-observer", new { enabled = false, error = ex.GetType().Name });
        }

        follow.Start();
        colourSample.Start();
        Tick();
        SamplePetColour();
    }

    void Publish(bool visible)
    {
        var previous = Current;
        if (previous.Target == target && previous.Anchor == anchor && previous.Size == petSize && previous.AnchorKnown == anchorKnown &&
            previous.Visible == visible && previous.Dragging == drag.Active && previous.PetId == petSwitch.Pet && previous.Colour == colour)
            return;
        Current = new PetState(target, anchor, petSize, anchorKnown, visible, drag.Active, petSwitch.Pet, colour);
        Publications++;
        var handler = Changed;
        if (handler != null)
            handler(Current);
    }

    public void Dispose()
    {
        if (closing)
            return;
        closing = true;
        if (metadataWatcher != null)
        {
            metadataWatcher.Dispose();
            metadataWatcher = null;
        }

        metadataChanged.Stop();
        metadataChanged.Dispose();
        follow.Stop();
        colourSample.Stop();
        follow.Dispose();
        colourSample.Dispose();
        if (mouse != null)
        {
            mouse.Dispose();
            mouse = null;
        }

        drag.Reset();
        Changed = null;
        DiagnosticChanged = null;
    }

    void StartMetadataWatcher()
    {
        try
        {
            if (!Directory.Exists(home))
                return;
            metadataWatcher = new FileSystemWatcher(home);
            metadataWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            metadataWatcher.Changed += (s, e) => MetadataFileChanged(e.Name);
            metadataWatcher.Created += (s, e) => MetadataFileChanged(e.Name);
            metadataWatcher.Deleted += (s, e) => MetadataFileChanged(e.Name);
            metadataWatcher.Renamed += (s, e) =>
            {
                MetadataFileChanged(e.OldName);
                MetadataFileChanged(e.Name);
            };
            metadataWatcher.Error += (s, e) => QueueMetadata(3);
            metadataWatcher.EnableRaisingEvents = true;
        }
        catch (IOException)
        {
            StopMetadataWatcher();
        }
        catch (UnauthorizedAccessException)
        {
            StopMetadataWatcher();
        }
    }

    void StopMetadataWatcher()
    {
        if (metadataWatcher != null)
        {
            metadataWatcher.Dispose();
            metadataWatcher = null;
        }
    }

    void MetadataFileChanged(string name)
    {
        if (String.Equals(name, "config.toml", StringComparison.OrdinalIgnoreCase))
            QueueMetadata(1);
        else if (String.Equals(name, ".codex-global-state.json", StringComparison.OrdinalIgnoreCase))
            QueueMetadata(2);
    }

    void QueueMetadata(int changed)
    {
        lock (notificationGate)
        {
            if (closing)
                return;
            pendingMetadata |= changed;
            if ((changed & 1) != 0)
                notifiedSelectionReads = SelectionReads;
            if ((changed & 2) != 0)
                notifiedStateReads = StateReads;
            if (notificationQueued)
                return;
            notificationQueued = true;
        }

        try
        {
            dispatch(() =>
            {
                if (!closing)
                    metadataChanged.Start();
            });
        }
        catch (InvalidOperationException)
        {
            lock (notificationGate)
            {
                notificationQueued = false;
            }
        }
    }

    void SamplePetColour()
    {
        if (!petSwitch.CanSample(watch.ElapsedMilliseconds) || colourBusy || closing || !Current.Visible || target == IntPtr.Zero || !anchorKnown)
            return;
        colourBusy = true;
        var handle = target;
        var expectedAnchor = anchor;
        var generation = petSwitch.Generation;
        int size = petSize;
        Task.Factory.StartNew(() =>
        {
            Color? sample = null;
            try
            {
                Native.RECT r;
                if (Native.GetWindowRect(handle, out r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (w > 0 && h > 0 && w <= 8192 && h <= 8192)
                    {
                        var area = new Rectangle(expectedAnchor.X - r.Left, expectedAnchor.Y - r.Top, size, (int)Math.Ceiling(size * 208.0 / 192));
                        var clipped = Rectangle.Intersect(area, new Rectangle(0, 0, w, h));
                        if (clipped.Width * clipped.Height >= area.Width * area.Height * .8)
                        {
                            using (var image = new Bitmap(w, h))
                            {
                                bool ok;
                                using (var g = Graphics.FromImage(image))
                                {
                                    var dc = g.GetHdc();
                                    try
                                    {
                                        ok = Native.PrintWindow(handle, dc, 2);
                                    }
                                    finally
                                    {
                                        g.ReleaseHdc(dc);
                                    }
                                }

                                if (ok)
                                    sample = PetTheme.Dominant(image, clipped);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            if (closing)
                return;
            try
            {
                dispatch(() =>
                {
                    if (closing)
                        return;
                    colourBusy = false;
                    ReadSelection();
                    if (generation != petSwitch.Generation)
                    {
                        Program.Record("pet-colour-discarded", new { generation = generation, currentGeneration = petSwitch.Generation, reason = "selection changed" });
                        return;
                    }

                    if (target != handle || anchor != expectedAnchor)
                        sample = null;
                    if (!petSwitch.AcceptSample(generation, sample, watch.ElapsedMilliseconds))
                        return;
                    ApplyPetColour(sample.Value, "pet-window-render-confirmed");
                });
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    void ReadSelection()
    {
        string pet = null;
        try
        {
            var file = new FileInfo(Path.Combine(home, "config.toml"));
            if (!file.Exists)
                selectionLoaded = false;
            if (file.Exists && (!selectionLoaded || file.LastWriteTimeUtc != selectionFileTime || file.Length != selectionFileLength))
            {
                SelectionReads++;
                int? size;
                if (PetSelectionConfig.TryRead(file.FullName, out pet, out size))
                {
                    selectionFileTime = file.LastWriteTimeUtc;
                    selectionFileLength = file.Length;
                    selectionLoaded = true;
                    if (size.HasValue)
                        petSizeDip = size.Value;
                }
                else
                    pet = null;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (petSwitch.Select(pet, watch.ElapsedMilliseconds))
        {
            Program.Record("pet-selection", new { pet = petSwitch.Pet, generation = petSwitch.Generation });
        }

        if (petSwitch.HasColour)
            return;
        Color colour;
        if (palettes.TryGet(petSwitch.Pet, out colour) && petSwitch.ApplySource(petSwitch.Generation, colour))
            ApplyPetColour(colour, "sprite-source-catalog");
    }

    void ApplyPetColour(Color value, string source)
    {
        colour = value;
        Program.Record("pet-theme", new { source = source, r = value.R, g = value.G, b = value.B, pet = petSwitch.Pet, generation = petSwitch.Generation });
    }

    void ReadAnchor()
    {
        var path = Path.Combine(home, ".codex-global-state.json");
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                stateError = "missing";
                return;
            }

            var modified = file.LastWriteTimeUtc;
            if (modified == fileTime && stateError == null)
                return;
            Dictionary<string, object> root;
            StateReads++;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            if (root == null)
                throw new ArgumentException("Invalid pet state");
            var b = Find(root, "electron-avatar-overlay-bounds") as Dictionary<string, object>;
            var open = Find(root, "electron-avatar-overlay-open");
            petOpen = open is bool && (bool)open;
            anchorKnown = false;
            if (b != null && b.ContainsKey("x") && b.ContainsKey("y"))
            {
                savedAnchorDip = new Point(Convert.ToInt32(b["x"]), Convert.ToInt32(b["y"]));
                savedDisplayBounds = ReadDisplayBounds(b);
                anchorKnown = true;
            }

            fileTime = modified;
            stateError = null;
        }
        catch (IOException e)
        {
            stateError = e.GetType().Name;
        }
        catch (UnauthorizedAccessException e)
        {
            stateError = e.GetType().Name;
        }
        catch (ArgumentException e)
        {
            stateError = e.GetType().Name;
        }
        catch (InvalidOperationException e)
        {
            stateError = e.GetType().Name;
        }
        catch (FormatException e)
        {
            stateError = e.GetType().Name;
        }
        catch (OverflowException e)
        {
            stateError = e.GetType().Name;
        }
    }

    static object Find(Dictionary<string, object> d, string key)
    {
        object v;
        if (d.TryGetValue(key, out v))
            return v;
        foreach (var x in d.Values)
        {
            var child = x as Dictionary<string, object>;
            if (child != null)
            {
                v = Find(child, key);
                if (v != null)
                    return v;
            }
        }

        return null;
    }

    internal void RefreshDisplayMetrics()
    {
        if (closing || !started || displayRefreshQueued)
            return;
        displayRefreshQueued = true;
        // Defer out of WndProc: publishing a new position can itself resize a
        // window and deliver more display messages.
        dispatch(() =>
        {
            displayRefreshQueued = false;
            if (!closing)
            {
                nextMetadata = 0;
                nextDisplayRead = 0;
                Tick();
            }
        });
    }

    void UpdatePhysicalCoordinates(Point? pointer = null)
    {
        uint nextDpi = target == IntPtr.Zero ? dpi : getWindowDpi(target);
        // A transient invalid HWND must not switch a scaled desktop to 100%.
        if (nextDpi == 0)
            nextDpi = dpi;
        if (nextDpi != dpi)
        {
            Program.Record("pet-dpi", new { previous = dpi, current = nextDpi });
            dpi = nextDpi;
            nextDisplayRead = 0;
        }

        if (watch.ElapsedMilliseconds >= nextDisplayRead)
        {
            nextDisplayRead = watch.ElapsedMilliseconds + 1000;
            try
            {
                desktop = readDesktop(target, dpi);
                if (desktop == null)
                    throw new InvalidOperationException("Monitor metrics unavailable");
                if (drag.Display != null)
                {
                    var current = desktop.Find(drag.Display.Id);
                    if (current == null || (!drag.Active && current.Logical.Bounds.Location != drag.Display.Bounds.Location))
                        drag.Reset(); // Observe the next saved host position; do not relocate Pet.
                    else
                        drag.RefreshDisplays(desktop.LogicalDisplays, current.Logical);
                }

                displayError = null;
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is InvalidOperationException) &&
                    !(ex is System.ComponentModel.Win32Exception))
                    throw;
                if (displayError != ex.Message)
                    Program.Record("pet-display-unavailable", new { error = ex.Message });
                displayError = ex.Message;
                desktop = null;
                renderedDisplay = null;
                drag.Reset();
                return;
            }
        }

        ProjectAnchor();
    }

    static Rectangle? ReadDisplayBounds(Dictionary<string, object> saved)
    {
        object value;
        if (!saved.TryGetValue("displayBounds", out value))
            return null;
        var bounds = value as Dictionary<string, object>;
        if (bounds == null || !bounds.ContainsKey("x") || !bounds.ContainsKey("y") ||
            !bounds.ContainsKey("width") || !bounds.ContainsKey("height"))
            return null;
        int width = Convert.ToInt32(bounds["width"]), height = Convert.ToInt32(bounds["height"]);
        return width > 0 && height > 0 ? (Rectangle? )new Rectangle(Convert.ToInt32(bounds["x"]),
            Convert.ToInt32(bounds["y"]), width, height) : null;
    }

    void ProjectAnchor()
    {
        if (desktop == null)
            return;
        var savedDisplay = savedDisplayBounds.HasValue
            ? desktop.Displays.Find(d => d.Logical.Bounds == savedDisplayBounds.Value) : null;
        var logical = drag.Resolve(savedAnchorDip, watch.ElapsedMilliseconds);
        if (savedDisplayBounds.HasValue && savedDisplay == null && !drag.HasPosition)
        {
            // A saved layout from before hotplug/metrics changes is not a live
            // host position. Wait for Codex, rather than restore it ourselves.
            renderedDisplay = null;
            return;
        }

        savedDisplay = savedDisplay ?? desktop.Nearest(savedAnchorDip, false);
        savedAnchor = savedDisplay.Coordinates.ToPhysical(savedAnchorDip);
        renderedDisplay = drag.HasPosition && drag.Display != null ? desktop.Find(drag.Display.Id) : savedDisplay;
        renderedDisplay = renderedDisplay ?? savedDisplay;
        anchor = renderedDisplay.Coordinates.ToPhysical(logical);
        petSize = renderedDisplay.Coordinates.ToPhysicalLength(petSizeDip);
    }

    void Tick()
    {
        if (closing)
            return;
        // The idle timer already limits this path to 100 ms. Applying another
        // deadline can skip a slightly early/jittered tick and produce 200 ms gaps.
        if (!drag.Active || watch.ElapsedMilliseconds >= nextMetadata)
        {
            nextMetadata = watch.ElapsedMilliseconds + 100;
            ReadSelection();
            ReadAnchor();
        }

        // Missing pets still use the discovery interval; do not enumerate all
        // desktop windows at 16 ms just because the last target was zero.
        if (watch.ElapsedMilliseconds >= nextDiscovery || (target != IntPtr.Zero && !Native.IsWindow(target)))
        {
            nextDiscovery = watch.ElapsedMilliseconds + 2000;
            var candidates = discoverWindows();
            candidateCount = candidates.Count;
            IntPtr found = candidates.Count == 1 ? candidates[0] : IntPtr.Zero;
            if (found != target)
            {
                drag.Reset();
                target = found;
                nextDisplayRead = 0;
                Program.Record("target", new { hwnd = target.ToInt64(), candidates = candidates.Count });
            }
        }

        // Poll the foreign window as well as responding to our own DPI messages.
        // Its notification may arrive later, or while our bubble is hidden.
        UpdatePhysicalCoordinates();
        bool visible = renderedDisplay != null && desktop != null && anchorKnown && petOpen && target != IntPtr.Zero && Native.IsWindowVisible(target) && !Native.IsIconic(target);
        if (!visible)
        {
            drag.Reset();
            anchor = savedAnchor;
            Publish(false);
            UpdateDiagnostic();
            follow.Interval = 100;
            return;
        }

        if (drag.Active)
        {
            Native.POINT cursor;
            if (Native.GetCursorPos(out cursor))
                MoveDrag(new Point(cursor.X, cursor.Y));
            // Also recover if Windows omitted a hook release event.
            int button = Native.GetSystemMetrics(23) != 0 ? 2 : 1;
            if ((Native.GetAsyncKeyState(button) & 0x8000) == 0)
            {
                drag.End(watch.ElapsedMilliseconds);
                dragReleased = true;
            }
        }

        ProjectAnchor();
        if (dragStarted)
        {
            dragStarted = false;
            Program.Record("drag-start", new { anchorX = anchor.X, anchorY = anchor.Y });
        }

        if (dragReleased)
        {
            dragReleased = false;
            Program.Record("drag-release", new { anchorX = anchor.X, anchorY = anchor.Y });
        }

        Publish(true);
        UpdateDiagnostic();
        follow.Interval = drag.Active ? 16 : 100;
    }

    void UpdateDiagnostic()
    {
        bool windowVisible = target != IntPtr.Zero && Native.IsWindowVisible(target) && !Native.IsIconic(target);
        var next = PetTrackingDiagnostic.Create(stateError, anchorKnown, petOpen, candidateCount, windowVisible);
        if (next.Reason == "ready" && displayError != null)
        {
            next.Reason = "display-unavailable";
            next.StateError = displayError;
        }
        else if (next.Reason == "ready" && desktop != null && renderedDisplay == null)
            next.Reason = "display-position-unavailable";
        if (next.SameAs(Diagnostic))
            return;
        Diagnostic = next;
        var handler = DiagnosticChanged;
        if (handler != null)
            handler(next);
    }

    static List<IntPtr> DiscoverWindows()
    {
        var candidates = new List<IntPtr>();
        Native.EnumWindows((hwnd, param) =>
        {
            var cls = new StringBuilder(128);
            Native.GetClassName(hwnd, cls, cls.Capacity);
            long ex = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
            if (cls.ToString() == "Chrome_WidgetWin_1" && (ex & 0x80008) == 0x80008)
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                        if (CodexDesktopSession.Matches(p.ProcessName, p.MainModule.FileName))
                            candidates.Add(hwnd);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    void MouseChanged(Point pointer, bool down)
    {
        if (closing)
            return;
        UpdatePhysicalCoordinates(pointer);
        if (down)
        {
            if ((Native.GetAsyncKeyState(17) & 0x8000) != 0)
                return;
            if (desktop == null || renderedDisplay == null || !anchorKnown || !petOpen || target == IntPtr.Zero || !Native.IsWindowVisible(target))
                return;
            var pet = new Rectangle(anchor.X, anchor.Y, petSize, (int)Math.Ceiling(petSize * 208.0 / 192));
            if (!pet.Contains(pointer))
                return;
            var p = new Native.POINT
            {
                X = pointer.X,
                Y = pointer.Y
            };
            if (Native.GetAncestor(Native.WindowFromPoint(p), 2) != target)
                return;
            BeginObservedDrag(pointer);
        }
        else if (drag.Active)
        {
            MoveDrag(pointer);
            drag.End(watch.ElapsedMilliseconds);
            dragReleased = true;
        }
    }

    void MoveDrag(Point pointer)
    {
        if (desktop == null)
            return;
        var physicalDisplay = desktop.Nearest(pointer, true);
        var logicalPointer = physicalDisplay.Coordinates.PointerToLogical(pointer);
        var nearest = desktop.Nearest(logicalPointer, false);
        drag.Move(logicalPointer, nearest.Logical, petSizeDip);
    }

    void BeginObservedDrag(Point pointer)
    {
        var pointerDisplay = desktop.Nearest(pointer, true);
        drag.Begin(pointerDisplay.Coordinates.PointerToLogical(pointer),
            renderedDisplay.Coordinates.ToLogical(anchor), savedAnchorDip, pointerDisplay.Logical);
        dragStarted = true;
        follow.Interval = 16;
    }
}
