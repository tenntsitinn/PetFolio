using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Exercise production mouse handlers on an independent window without moving
// the user's cursor, generating system input or reading a real account.
static class BubbleClickTests
{
    static int checks;
    static string settingsError;
    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message + (settingsError == null ? "" : " Settings diagnostic: " + settingsError));
        checks++;
    }

    sealed class Pets : IPetStateSource
    {
        public PetState Current
        {
            get
            {
                return new PetState(new IntPtr(1), new Point(600, 400), 112, true, true, false, "test", Color.Gold);
            }
        }

        public event Action<PetState> Changed
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    sealed class Source : IQuotaSource
    {
        public int Calls;
        public readonly AutoResetEvent Entered = new AutoResetEvent(false), Release = new AutoResetEvent(true);
        public bool Fail;
        public QuotaSnapshot Read()
        {
            Interlocked.Increment(ref Calls);
            Entered.Set();
            if (!Release.WaitOne(5000))
                throw new Exception("Test did not release source");
            if (Fail)
                throw new Exception("Offline");
            return new QuotaSnapshot(new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc), new QuotaWindow(20, 300, null), null);
        }

        public void Dispose()
        {
            Release.Set();
        }
    }

    static void Mouse(QuotaLabel view, string method, int x, int y, MouseButtons button = MouseButtons.Left)
    {
        typeof(QuotaLabel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, new object[] { new MouseEventArgs(button, 1, x, y, 0) });
    }

    static string Text(QuotaLabel view, string field)
    {
        return (string)typeof(QuotaLabel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
    }

    static Color CloseInk(QuotaLabel view)
    {
        return (Color)typeof(CloseBubbleButton).GetField("ink", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view.DismissButton);
    }

    static QuotaLabel LoadSavedView()
    {
        var settings = new BubbleSettingsStore(System.IO.Path.Combine(Program.DataFolder, "appearance.json"), (k, d) =>
        {
            settingsError = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
        }).Load();
        return new QuotaLabel("", settings, new BubbleContentState(settings));
    }

    static void Click(QuotaLabel view, int x = 80, int y = 40)
    {
        var point = new IntPtr(x | (y << 16));
        SendMessage(view.Handle, 0x201, new IntPtr(1), point);
        SendMessage(view.Handle, 0x202, IntPtr.Zero, point);
        FlushClick();
    }

    static void FlushClick()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SystemInformation.DoubleClickTime + 80);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [STAThread]
    static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(e.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var source = new Source();
        var pending = new ConcurrentQueue<Action>();
        using (var posted = new AutoResetEvent(false))
        using (var feature = new QuotaFeature("", new Pets(), () => source, a =>
        {
            pending.Enqueue(a);
            posted.Set();
        }, s =>
        {
        }, (k, d) =>
        {
            if (k == "appearance-error") settingsError = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
        }))
        {
            Action drain = () =>
            {
                Check(posted.WaitOne(5000), "Query completion arrives");
                Action a;
                while (pending.TryDequeue(out a))
                    a();
            };
            feature.Start();
            Check(source.Entered.WaitOne(5000), "Initial request starts");
            drain();
            var view = feature.View;
            int clicks = 0;
            view.RefreshRequested += () =>
            {
                if (!view.SystemMode)
                    clicks++;
            };
            var interactionTimer = (System.Windows.Forms.Timer)typeof(QuotaLabel).GetField("interactionTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Check(interactionTimer.Enabled && interactionTimer.Interval == 100, "Visible stationary bubble maintains a low-frequency hover timer");
            view.SetDefaultAppearance(false);
            Check(view.ClientSize == new Size(208, 104), "Approved compact geometry is used");
            if (view.Owner is GlassBackdrop)
                Check(view.Owner.ClientSize == view.ClientSize, "Glass and foreground share dimensions");
            view.SetMode(false);
            view.UpdatePet(new PetState(new IntPtr(1), new Point(600, 400), 112, true, true, false, "dark-test", Color.FromArgb(35, 30, 48)));
            typeof(QuotaLabel).GetMethod("SetOpacity", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 20 });
            int backgroundReads = 0;
            Color? background = Color.White;
            view.ReadBackground = (bounds, tint) =>
            {
                backgroundReads++;
                Check(bounds == view.Bounds, "Sampling uses the current bubble position");
                return background;
            };
            var amount = Text(view, "value1");
            Click(view);
            Check(source.Entered.WaitOne(5000), "Single click starts a real feature refresh");
            Check(clicks == 1 && Text(view, "status") == "Updating..." && Text(view, "value1") == amount, "Click shows busy feedback while preserving the amount");
            Check(backgroundReads == 1 && view.UsesBlackText, "Manual quota refresh detects light background and uses black text");
            Check(CloseInk(view).ToArgb() == Color.Black.ToArgb(), "Close glyph uses the same adaptive black ink after manual refresh");
            Click(view);
            Click(view);
            Check(source.Calls == 2, "Repeated clicks during refresh do not start extra queries");
            Check(backgroundReads == 1, "Coalesced clicks do not repeat background capture");
            source.Release.Set();
            drain();
            Check(Text(view, "status") == "updated " + new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm"), "Successful refresh displays hours and minutes");
            Check(backgroundReads == 1, "Query completion does not sample a second time");
            background = Color.Black;
            var service = (QuotaService)typeof(QuotaFeature).GetField("quota", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature);
            var timer = (System.Windows.Forms.Timer)typeof(QuotaService).GetField("refresh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(timer, new object[] { EventArgs.Empty });
            Check(source.Entered.WaitOne(5000) && backgroundReads == 2 && !view.UsesBlackText, "Automatic timer refresh notices dark background and restores white text");
            Check(CloseInk(view).ToArgb() == Color.White.ToArgb(), "Unchanged tint still updates the close glyph when desktop changes");
            source.Release.Set();
            drain();
            background = Color.White;
            ((ToolStripMenuItem)feature.Commands[feature.Commands.Length - 1]).PerformClick();
            Check(source.Entered.WaitOne(5000) && backgroundReads == 3 && view.UsesBlackText, "Tray refresh notices background becoming light again");
            Check(CloseInk(view).ToArgb() == Color.Black.ToArgb(), "Tray refresh synchronizes close glyph ink");
            source.Release.Set();
            drain();
            background = Color.Black;
            typeof(QuotaLabel).GetMethod("SetOpacity", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 30 });
            Check(backgroundReads == 4 && !view.UsesBlackText, "Applying new opacity samples once and keeps white text at 30%");
            background = Color.White;
            typeof(QuotaLabel).GetMethod("SetOpacity", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 20 });
            Check(backgroundReads == 5 && view.UsesBlackText, "Applying 20% immediately uses the current light background");
            int readsBeforeMovement = backgroundReads;
            int before = clicks;
            Mouse(view, "OnMouseDown", 80, 40);
            Mouse(view, "OnMouseMove", 90, 40);
            Mouse(view, "OnMouseMove", 80, 40);
            Mouse(view, "OnMouseUp", 80, 40);
            Check(clicks == before, "Dragging away and back never becomes a click");
            Check(interactionTimer.Interval == 16, "Panel drag and snap use the fast interaction interval");
            var spring = (PetPanelSpring)typeof(QuotaLabel).GetField("snap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var settle = System.Diagnostics.Stopwatch.StartNew();
            while (spring.Active && settle.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            Check(!spring.Active && interactionTimer.Interval == 100, "Snap settles without any new pet snapshots and returns to idle hover timing");
            Mouse(view, "OnMouseDown", 80, 40);
            view.Capture = false;
            Mouse(view, "OnMouseUp", 80, 40);
            Check(clicks == before, "Capture loss cancels refresh");
            Mouse(view, "OnMouseDown", 80, 40, MouseButtons.Right);
            Mouse(view, "OnMouseUp", 80, 40, MouseButtons.Right);
            Check(clicks == before, "Right click does not refresh");
            Mouse(view, "OnMouseDown", 80, 40);
            Mouse(view, "OnMouseUp", -1, 40);
            Check(clicks == before, "Release outside the body does not refresh");
            Mouse(view, "OnMouseDown", 0, 22);
            Mouse(view, "OnMouseUp", 0, 19);
            Check(clicks == before, "Release outside the rounded corner does not refresh even below the drag threshold");
            Check(backgroundReads == readsBeforeMovement, "Movement, drag, cancelled clicks and repaint never sample background");
            view.PreviewHover(new Point(view.Left + 80, view.Top + 40));
            SendMessage(view.DismissButton.Handle, 0x201, new IntPtr(1), new IntPtr(10 | (10 << 16)));
            SendMessage(view.DismissButton.Handle, 0x202, IntPtr.Zero, new IntPtr(10 | (10 << 16)));
            Check(!view.BubbleRequested && clicks == before, "Close button hides without refreshing");
            Check(!interactionTimer.Enabled, "Hidden bubble stops its interaction timer");
            ((ToolStripMenuItem)feature.Commands[feature.Commands.Length - 1]).PerformClick();
            Check(source.Entered.WaitOne(5000) && backgroundReads == readsBeforeMovement, "Hidden bubble keeps quota polling without capturing a background");
            source.Release.Set();
            drain();
            feature.Restore();
            before = clicks; // Tray and body now share the same refresh dispatch.
            background = null;
            source.Fail = true;
            Mouse(view, "OnMouseDown", 80, 40);
            Mouse(view, "OnMouseMove", 83, 43);
            Mouse(view, "OnMouseUp", 83, 43);
            FlushClick();
            Check(source.Entered.WaitOne(5000) && clicks == before + 1, "Sub-threshold pointer jitter still refreshes");
            source.Release.Set();
            drain();
            Check(Text(view, "status").StartsWith("Failed · last ") && Text(view, "value1") == amount, "Failure retains amount and shows last successful time");
            Check(view.UsesBlackText && backgroundReads == readsBeforeMovement + 1, "Capture failure preserves last known background without blocking quota error feedback");
            source.Fail = false;
            Click(view);
            Check(source.Entered.WaitOne(5000), "Failed refresh can be retried by clicking");
            source.Release.Set();
            drain();
            Check(Text(view, "status").StartsWith("updated "), "Retry clears error feedback");
            before = clicks;
            var hwnd = view.Handle;
            var size = view.Size;
            var point = new IntPtr(80 | (40 << 16));
            SendMessage(hwnd, 0x201, new IntPtr(1), point);
            SendMessage(hwnd, 0x202, IntPtr.Zero, point);
            SendMessage(hwnd, 0x203, new IntPtr(1), point);
            SendMessage(hwnd, 0x202, IntPtr.Zero, point);
            FlushClick();
            Check(view.SystemMode && clicks == before, "Double click switches to System without a quota refresh");
            Check(view.Handle == hwnd && view.Size == size, "Switch reuses the same window and geometry");
            using (var restored = LoadSavedView())
                Check(restored.SystemMode, "A new instance restores the saved System mode");
            Click(view);
            Check(clicks == before, "System single click never requests quota");
            var systemTimer = (System.Windows.Forms.Timer)typeof(SystemMetricsService).GetField("sampleTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature.SystemService);
            Check(systemTimer.Enabled, "System mode samples while visible");
            var systemTick = typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic);
            background = Color.White;
            systemTick.Invoke(systemTimer, new object[] { EventArgs.Empty });
            Check(view.UsesBlackText, "System refresh switches low-opacity ink to black on a light desktop");
            Check(CloseInk(view).ToArgb() == Color.Black.ToArgb(), "System sampling synchronizes close glyph to black");
            background = Color.Black;
            systemTick.Invoke(systemTimer, new object[] { EventArgs.Empty });
            Check(!view.UsesBlackText, "System refresh switches low-opacity ink to white on a dark desktop");
            Check(CloseInk(view).ToArgb() == Color.White.ToArgb(), "System sampling synchronizes close glyph to white");
            background = null;
            systemTick.Invoke(systemTimer, new object[] { EventArgs.Empty });
            Check(!view.UsesBlackText, "Failed system background capture preserves the last ink choice");
            Check(CloseInk(view).ToArgb() == Color.White.ToArgb(), "Failed capture preserves matching close glyph ink");
            view.SetBubbleVisible(false);
            Check(!systemTimer.Enabled, "Hiding stops system sampling");
            int hiddenReads = backgroundReads;
            systemTick.Invoke(systemTimer, new object[] { EventArgs.Empty });
            Check(backgroundReads == hiddenReads, "Hidden system refresh never captures the desktop");
            feature.Restore();
            Check(view.SystemMode && systemTimer.Enabled, "Restore retains mode and resumes sampling");
            view.SetMode(false);
            Check(!systemTimer.Enabled && view.Handle == hwnd, "Returning to Quota stops sampling and reuses window");
            Check(Text(view, "value1") == amount, "Switching preserves latest quota snapshot");
            var quotaOption = (ToolStripMenuItem)feature.FeatureOptions[0];
            var systemOption = (ToolStripMenuItem)feature.FeatureOptions[1];
            var refresh = (ToolStripMenuItem)feature.Commands[feature.Commands.Length - 1];
            var switchMode = (ToolStripMenuItem)view.Commands[2];
            Check(view.ShowBubbleCommand.Text == "Show Bubble" && refresh.Text == "Refresh", "Tray actions use stable mode-independent labels");
            quotaOption.PerformClick();
            Check(!view.QuotaEnabled && view.SystemEnabled && view.SystemMode && !quotaOption.Checked && systemOption.Checked, "Disabling active Quota selects System and updates checkmarks");
            Check(typeof(QuotaFeature).GetField("quota", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature) == null, "Disabling Quota releases its polling service");
            Check(!switchMode.Available && !switchMode.Enabled, "Single enabled mode removes the switch command");
            int callsBeforeSystemRefresh = source.Calls, readsBeforeSystemRefresh = backgroundReads;
            background = Color.White;
            refresh.PerformClick();
            Check(source.Calls == callsBeforeSystemRefresh && backgroundReads == readsBeforeSystemRefresh + 1 && view.UsesBlackText, "System tray refresh updates background without querying quota");
            SendMessage(hwnd, 0x201, new IntPtr(1), point);
            SendMessage(hwnd, 0x202, IntPtr.Zero, point);
            SendMessage(hwnd, 0x203, new IntPtr(1), point);
            SendMessage(hwnd, 0x202, IntPtr.Zero, point);
            FlushClick();
            Check(view.SystemMode && source.Calls == callsBeforeSystemRefresh, "Double-click cannot enter a disabled quota mode");
            systemOption.PerformClick();
            Check(view.Visible && systemTimer.Enabled && systemOption.Checked && refresh.Enabled, "Last System option remains checked and running");
            feature.Restore();
            Check(view.Visible && view.AnyModeEnabled, "Restore retains the remaining feature");
            using (var restored = LoadSavedView())
                Check(!restored.QuotaEnabled && restored.SystemEnabled, "Rejected toggle preserves settings after restart");
            systemOption.PerformClick();
            Check(view.Visible && view.SystemMode && systemTimer.Enabled && refresh.Enabled, "Re-enabling System restores the retained visible bubble and sampling");
            quotaOption.PerformClick();
            Check(view.SystemMode && switchMode.Available && quotaOption.Checked, "Enabling another mode does not steal the current display");
            Check(source.Entered.WaitOne(5000), "Re-enabling Quota starts polling again");
            drain();
            systemOption.PerformClick();
            Check(!view.SystemMode && !systemTimer.Enabled, "Disabling active System falls back to Quota and stops sampling");
            int callsBeforeRejectedToggle = source.Calls;
            quotaOption.PerformClick();
            Check(view.QuotaEnabled && !view.SystemEnabled && quotaOption.Checked && !view.SystemMode && source.Calls == callsBeforeRejectedToggle,
                "Last Quota option remains checked without restarting quota polling");
            view.SetMode(true);
            Check(!view.SystemMode, "Disabled System cannot be selected programmatically");
            systemOption.PerformClick();
            Check(!view.SystemMode && view.QuotaEnabled && view.SystemEnabled, "Re-enable preserves Quota selection and restores both defaults");
            var appearance = (ToolStripMenuItem)view.Commands[0];
            var defaultItem = (ToolStripMenuItem)appearance.DropDownItems[0];
            var petItem = (ToolStripMenuItem)appearance.DropDownItems[1];
            var opacity = (MenuCaption)appearance.DropDownItems[5];
            var colourField = typeof(QuotaLabel).GetField("panelColor", BindingFlags.Instance | BindingFlags.NonPublic);
            bool? darkTheme = false;
            view.ReadCodexTheme = () => darkTheme;
            defaultItem.PerformClick();
            var glass = view.Owner as GlassBackdrop;
            if (glass != null)
                Check(!glass.BlurEnabled, "Default removes background blur from composition");
            Check(appearance.Text == "Appearance" && defaultItem.Checked && !petItem.Checked && !opacity.Enabled, "Default selection checks one appearance and locks opacity");
            Check((Color)colourField.GetValue(view) == Color.FromArgb(230, 255, 255, 255), "Light Default uses native white at fixed E6 alpha");
            Check(CloseInk(view).ToArgb() == BubbleAppearance.Text(false).ToArgb(), "Light Default shares exact native text RGBA with close glyph");
            typeof(QuotaLabel).GetMethod("SetOpacity", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 100 });
            Check(((Color)colourField.GetValue(view)).A == 230, "Default ignores direct opacity changes");
            darkTheme = true;
            view.RefreshCodexAppearance();
            Check((Color)colourField.GetValue(view) == Color.FromArgb(230, 32, 32, 32), "Theme refresh switches Default to the native dark surface");
            Check(CloseInk(view).ToArgb() == BubbleAppearance.Text(true).ToArgb(), "Dark Default shares exact native text RGBA with close glyph");
            darkTheme = null;
            view.RefreshCodexAppearance();
            Check((Color)colourField.GetValue(view) == Color.FromArgb(230, 32, 32, 32), "Theme read failure retains the current palette");
            view.UpdatePet(new PetState(new IntPtr(1), new Point(600, 400), 112, true, true, false, "new-pet", Color.CornflowerBlue));
            Check((Color)colourField.GetValue(view) == Color.FromArgb(230, 32, 32, 32), "Pet changes cannot recolour Default");
            int defaultReads = backgroundReads;
            view.SetMode(true);
            view.RefreshCurrent();
            Check(backgroundReads == defaultReads, "Default never uses desktop sampling to change text colours");
            using (var restored = LoadSavedView())
                Check(restored.DefaultAppearance, "Default appearance survives restart");
            petItem.PerformClick();
            if (glass != null)
                Check(glass.BlurEnabled, "Pet colour restores background blur");
            Check(opacity.Enabled && petItem.Checked && !defaultItem.Checked, "Pet colour unlocks opacity");
            Check((Color)colourField.GetValue(view) == Color.FromArgb(51, Color.CornflowerBlue), "Returning to Pet colour restores 20 percent with the latest pet colour");
            var slider = (OpacitySlider)((ToolStripControlHost)appearance.DropDownItems[6]).Control;
            Check(slider.Minimum == 0 && slider.Maximum == 100 && slider.SmallChange == 1 && slider.LargeChange == 1,
                "Opacity slider retains the range with one-percent increments");
            typeof(OpacitySlider).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new KeyEventArgs(Keys.Right) });
            Check(slider.Value == 21, "Keyboard moves the custom slider by one percent");
            typeof(OpacitySlider).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new KeyEventArgs(Keys.Home) });
            Check(slider.Value == 0 && slider.AccessibilityObject.Value == "0%", "Home reaches zero and exposes the accessible value");
            typeof(OpacitySlider).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new KeyEventArgs(Keys.End) });
            Check(slider.Value == 100, "End reaches full opacity");
            typeof(OpacitySlider).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, slider.Width / 2, slider.Height / 2, 0) });
            Check(slider.Value == 50, "Clicking the middle of the rail selects fifty percent");
            typeof(OpacitySlider).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 0, slider.Width + 100, slider.Height / 2, 0) });
            Check(slider.Value == 100, "Dragging past the rail clamps to the endpoint");
            typeof(OpacitySlider).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, slider.Width, slider.Height / 2, 0) });
            slider.Value = 17;
            Check(((Color)colourField.GetValue(view)).A == 43 && opacity.Text == "Opacity: 17%",
                "Slider previews an intermediate percentage and displays its value");
            using (var restored = LoadSavedView())
            {
                var saved = new BubbleSettings();
                restored.CopyAppearanceTo(saved);
                Check(saved.OpacityPercent == 17, "Intermediate slider percentage survives restart");
            }
            slider.Value = 20;
            using (var restored = LoadSavedView())
                Check(!restored.DefaultAppearance, "Pet appearance survives restart");
            var frosted = (ToolStripMenuItem)appearance.DropDownItems[3];
            Check(frosted.Checked && view.FrostedGlass, "Pet colour starts with frosted glass enabled");
            frosted.PerformClick();
            Check(!frosted.Checked && !view.FrostedGlass && (glass == null || !glass.BlurEnabled), "Frosted glass can be disabled from the menu");
            defaultItem.PerformClick();
            Check(!frosted.Checked && !frosted.Enabled, "Default disables and unchecks frosted glass");
            frosted.PerformClick();
            view.SetFrostedGlass(true);
            Check(!frosted.Checked && !view.FrostedGlass && !opacity.Enabled, "Default rejects blur activation including direct calls");
            Check(!slider.Enabled, "Default disables the custom opacity control");
            using (var restored = LoadSavedView())
                Check(restored.DefaultAppearance && !restored.FrostedGlass, "Default remains unblurred after restart");
            petItem.PerformClick();
            Check(!frosted.Checked && !view.FrostedGlass, "Returning to Pet colour restores its disabled blur choice");
            using (var restored = LoadSavedView())
                Check(!restored.DefaultAppearance && !restored.FrostedGlass, "Pet blur choice survives restart");
            frosted.PerformClick();
            defaultItem.PerformClick();
            frosted.PerformClick();
            petItem.PerformClick();
            view.SetMode(false);
            var area = Screen.PrimaryScreen.WorkingArea;
            int centerX = (int)Math.Ceiling(area.Left + area.Width / 2.0 - 56);
            int centerY = area.Top + area.Height / 2 - 61;
            Action<int, bool> movePet = (x, dragging) => view.UpdatePet(new PetState(new IntPtr(1),
                new Point(x, centerY), 112, true, true, dragging, "new-pet", Color.CornflowerBlue));
            Action settleLayout = () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (spring.Active && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                Check(!spring.Active, "Layout spring settles through the real UI timer");
            };
            movePet(centerX - 1, true);
            settleLayout();
            Check(view.Left == centerX - 1 + 112 + 12, "Pet drag left of work-area center places the real bubble on the right");
            var rigidSize = view.Size;
            movePet(centerX, true);
            Check(spring.Active && view.Right != centerX - 12 && interactionTimer.Interval == 16,
                "Pet drag across center starts a position spring instead of jumping");
            settleLayout();
            Check(view.Right == centerX - 12 && view.Size == rigidSize, "Spring settles on the left without resizing the bubble");
            movePet(centerX - 1, false);
            Check(view.Right == centerX - 1 - 12, "Idle snapshot retains the real bubble side across center");
            movePet(centerX - 1, true);
            Check(spring.Active, "Reverse pet drag animates the direction change");
            settleLayout();
            Check(view.Left == centerX - 1 + 112 + 12, "Reverse pet drag resets the real bubble direction");
            Check(view.Top == centerY + (122 - view.Height) / 2, "Side switching preserves canvas vertical centering");
        }

        Console.WriteLine(checks + " bubble click checks passed");
    }
}
