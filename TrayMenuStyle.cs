using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A noninteractive caption using the same text column as menu commands.
sealed class MenuCaption : ToolStripMenuItem
{
    internal MenuCaption(string text) : base(text) { TextAlign = ContentAlignment.MiddleLeft; }
    public override bool CanSelect { get { return false; } }
}

// Presentation only: existing ToolStrip items retain their events and state.
sealed class TrayMenuStyle : IDisposable
{
    readonly HashSet<ToolStripDropDown> menus = new HashSet<ToolStripDropDown>();
    readonly Font font = (Font)SystemFonts.MenuFont.Clone();
    readonly Renderer renderer = new Renderer();
    readonly Timer themeTimer = new Timer();
    readonly Timer submenuTimer = new Timer();
    readonly Dictionary<ToolStripDropDown, DateTime> outsideSince = new Dictionary<ToolStripDropDown, DateTime>();
    internal Func<bool?> ReadTheme;
    internal bool Dark
    {
        get
        {
            return renderer.Dark;
        }
    }

    public TrayMenuStyle(string home = null)
    {
        ReadTheme = () => CodexMenuTheme.Read(home, BubbleAppearance.ReadSystemDark);
        renderer.Dark = ReadTheme() ?? false;
        themeTimer.Interval = 2000;
        themeTimer.Tick += (s, e) => RefreshTheme();
        themeTimer.Start();
        submenuTimer.Interval = 100;
        submenuTimer.Tick += (s, e) => UpdateSubmenus(Cursor.Position);
        submenuTimer.Start();
    }

    internal void UpdateSubmenus(Point pointer)
    {
        foreach (var menu in menus)
        {
            if (menu.IsDisposed || !menu.Visible || menu.OwnerItem == null)
            {
                outsideSince.Remove(menu);
                continue;
            }
            var owner = menu.OwnerItem;
            bool insideOwner = owner.Owner != null && owner.Owner.RectangleToScreen(owner.Bounds).Contains(pointer);
            if (menu.Bounds.Contains(pointer) || insideOwner || menu.ContainsFocus && Control.MouseButtons != MouseButtons.None)
            {
                outsideSince.Remove(menu);
                continue;
            }
            DateTime since;
            if (!outsideSince.TryGetValue(menu, out since)) outsideSince[menu] = DateTime.UtcNow;
            else if ((DateTime.UtcNow - since).TotalMilliseconds >= 250)
                menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        }
    }

    internal void RefreshTheme()
    {
        var dark = ReadTheme();
        if (!dark.HasValue || dark.Value == renderer.Dark)
            return;
        renderer.Dark = dark.Value;
        foreach (var menu in menus)
            if (!menu.IsDisposed)
                Colour(menu);
    }

    void Colour(ToolStripDropDown menu)
    {
        menu.BackColor = renderer.Surface;
        menu.ForeColor = renderer.Text;
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = renderer.Text;
            var host = item as ToolStripControlHost;
            if (host != null)
            {
                host.Control.BackColor = renderer.Surface;
                host.Control.ForeColor = renderer.Text;
                var slider = host.Control as OpacitySlider;
                if (slider != null) slider.Dark = renderer.Dark;
            }
        }
        menu.Invalidate(true);
    }

    public void Apply(ToolStripDropDown menu)
    {
        if (menus.Add(menu))
        {
            menu.Font = font;
            menu.Renderer = renderer;
            menu.Closing += (s, e) =>
            {
                // Setting selection updates in place. Escape and outside clicks
                // retain the standard ToolStrip dismissal behavior.
                if (menu.OwnerItem != null && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                    e.Cancel = true;
            };
            menu.Opening += (s, e) =>
            {
                RefreshTheme();
                Layout(menu);
            };
            menu.SizeChanged += (s, e) => RoundWindow(menu);
            menu.LocationChanged += (s, e) =>
            {
                if (menu.Visible) PositionSubmenu(menu);
            };
            menu.Opened += (s, e) =>
            {
                Layout(menu);
                PositionSubmenu(menu);
                menu.BeginInvoke(new Action(() =>
                {
                    if (!menu.IsDisposed && menu.Visible) PositionSubmenu(menu);
                }));
                RoundWindow(menu);
            };
        }

        Layout(menu);
        Colour(menu);
        foreach (ToolStripItem item in menu.Items)
        {
            var child = item as ToolStripMenuItem;
            if (child != null && child.HasDropDownItems)
                Apply(child.DropDown);
        }
    }

    static void PositionSubmenu(ToolStripDropDown menu)
    {
        var item = menu.OwnerItem;
        var parent = item == null ? null : item.Owner as ToolStripDropDown;
        if (parent == null) return;
        var workArea = Screen.FromRectangle(parent.Bounds).WorkingArea;
        int top = parent.Top + item.Bounds.Top - parent.Padding.Top;
        var location = SubmenuLocation(parent.Bounds, menu.Size, workArea, top);
        if (menu.Location != location)
            SetWindowPos(menu.Handle, IntPtr.Zero, location.X, location.Y, 0, 0, 0x15);
    }

    internal static Point SubmenuLocation(Rectangle parent, Size child, Rectangle workArea, int top)
    {
        int x = parent.Right;
        if (x + child.Width > workArea.Right)
            x = parent.Left - child.Width;
        int y = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - child.Height));
        return new Point(x, y);
    }

    static float Scale(ToolStrip menu)
    {
        if (menu.IsHandleCreated)
        {
            try
            {
                return Math.Max(1, GetDpiForWindow(menu.Handle) / 96f);
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        using (var g = menu.CreateGraphics())
            return Math.Max(1, g.DpiX / 96f);
    }

    void Layout(ToolStripDropDown menu)
    {
        float scale = Scale(menu);
        int inset = (int)Math.Round(4 * scale), width = (int)Math.Round(208 * scale);
        menu.Padding = new Padding(inset);
        menu.Font = font;
        var root = menu;
        while (root.OwnerItem != null && root.OwnerItem.Owner is ToolStripDropDown)
            root = (ToolStripDropDown)root.OwnerItem.Owner;
        width = MeasureMenuTree(root, scale, width);
        int height = 2 * inset;
        foreach (ToolStripItem item in menu.Items)
        {
            item.Font = font;
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            bool hosted = item is ToolStripControlHost;
            // Hosted controls begin after the menu's check column. Reserve that
            // space rather than allowing the control to widen the popup.
            int itemWidth = hosted ? width - (int)Math.Round(32 * scale) : width;
            int itemHeight = (int)Math.Round((item is ToolStripSeparator ? 9 : 28) * scale);
            item.Size = new Size(itemWidth, itemHeight);
            if (item.Available) height += itemHeight;
        }
        menu.AutoSize = false;
        menu.Size = new Size(width + 2 * inset, height);
        RoundWindow(menu);
    }

    int MeasureMenuTree(ToolStripDropDown menu, float scale, int width)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            if (!item.Available || item is ToolStripSeparator)
                continue;
            int measured = TextRenderer.MeasureText(item.Text, font, Size.Empty, TextFormatFlags.SingleLine).Width;
            width = Math.Max(width, measured + (int)Math.Round(60 * scale));
            var child = item as ToolStripMenuItem;
            if (child != null && child.HasDropDownItems)
                width = MeasureMenuTree(child.DropDown, scale, width);
        }
        return width;
    }

    static void RoundWindow(ToolStripDropDown menu)
    {
        if (menu.Width <= 0 || menu.Height <= 0)
            return;
        // A window Region has binary coverage and prevents DWM rounding.
        // Let Windows 11 composite the outer edge; older systems retain the
        // existing region fallback without changing menu input behavior.
        if (menu.IsHandleCreated)
        {
            var previous = menu.Region;
            menu.Region = null;
            if (previous != null) previous.Dispose();
            try
            {
                int preference = 2; // DWMWCP_ROUND
                if (DwmSetWindowAttribute(menu.Handle, 33, ref preference, sizeof(int)) == 0)
                    return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        using (var path = Rounded(new RectangleF(0, 0, menu.Width, menu.Height), 8 * Scale(menu)))
        {
            var old = menu.Region;
            menu.Region = new Region(path);
            if (old != null)
                old.Dispose();
        }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    sealed class Renderer : ToolStripProfessionalRenderer
    {
        internal bool Dark;
        internal Color Surface
        {
            get
            {
                return Dark ? Color.FromArgb(40, 40, 40) : Color.White;
            }
        }

        internal Color Text
        {
            get
            {
                return Dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(13, 13, 13);
            }
        }

        Color Disabled
        {
            get
            {
                return Color.FromArgb(143, 143, 143);
            }
        }

        Color Edge
        {
            get
            {
                return Dark ? Color.FromArgb(68, 68, 68) : Color.FromArgb(216, 216, 216);
            }
        }

        Color Hover
        {
            get
            {
                return Dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(240, 240, 240);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Surface);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8 * Scale(e.ToolStrip)))
            using (var pen = new Pen(Edge))
                e.Graphics.DrawPath(pen, path);
            foreach (ToolStripItem item in e.ToolStrip.Items)
            {
                var expandable = item as ToolStripMenuItem;
                if (expandable == null || !expandable.Available || !expandable.HasDropDownItems) continue;
                float scale = Scale(e.ToolStrip), x = e.ToolStrip.ClientSize.Width - 16 * scale;
                float y = item.Bounds.Top + item.Height / 2f;
                var ink = item.Enabled ? (Dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(105, 105, 105)) : Disabled;
                using (var pen = new Pen(ink, 1.2f * scale))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - 2 * scale, y - 3 * scale), new PointF(x + scale, y), new PointF(x - 2 * scale, y + 3 * scale) });
                }
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(new RectangleF(0, 0, e.Item.Width, e.Item.Height), 4 * Scale(e.ToolStrip)))
            using (var brush = new SolidBrush(Hover))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            float scale = Scale(e.ToolStrip);
            int left = (int)Math.Round(28 * scale), right = (int)Math.Round(24 * scale);
            e.TextRectangle = new Rectangle(left, 0, Math.Max(1, e.Item.Width - left - right), e.Item.Height);
            var alignment = e.Item.TextAlign == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left;
            e.TextFormat = TextFormatFlags.VerticalCenter | alignment | TextFormatFlags.SingleLine;
            e.TextColor = e.Item.Enabled ? Text : Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            // Draw in popup coordinates in the final border pass, aligned to
            // the shared popup width rather than ToolStrip's text-sized slot.
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            float scale = Scale(e.ToolStrip), x = 13 * scale, y = e.Item.Height / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(e.Item.Enabled ? Text : Disabled, 1.5f * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[] { new PointF(x - 4 * scale, y), new PointF(x - 1 * scale, y + 3 * scale), new PointF(x + 5 * scale, y - 4 * scale) });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int inset = (int)Math.Round(8 * Scale(e.ToolStrip));
            using (var pen = new Pen(Edge))
                e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
        }
    }

    public void Dispose()
    {
        themeTimer.Dispose();
        submenuTimer.Dispose();
        outsideSince.Clear();
        menus.Clear();
        font.Dispose();
    }

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
