using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// A small menu control; values remain integer percentages, independent of pixels.
sealed class OpacitySlider : Control
{
    int value;
    bool dark;
    public event EventHandler ValueChanged;
    public int Minimum { get { return 0; } }
    public int Maximum { get { return 100; } }
    public int SmallChange { get { return 1; } }
    public int LargeChange { get { return 1; } }
    public int Value
    {
        get { return value; }
        set
        {
            int next = Math.Max(Minimum, Math.Min(Maximum, value));
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            var changed = ValueChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }
    }
    internal bool Dark
    {
        get { return dark; }
        set { dark = value; Invalidate(); }
    }

    public OpacitySlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        Size = new Size(176, 28);
    }

    float ScaleFactor { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }
    void SetFromPointer(int x)
    {
        float inset = 12 * ScaleFactor;
        Value = (int)Math.Round(100 * (x - inset) / Math.Max(1, ClientSize.Width - 2 * inset));
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        Capture = true;
        SetFromPointer(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && Enabled) SetFromPointer(e.X);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Capture = false;
    }
    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
        }
        return base.IsInputKey(keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: case Keys.PageDown: Value--; break;
            case Keys.Right: case Keys.Up: case Keys.PageUp: Value++; break;
            case Keys.Home: Value = Minimum; break;
            case Keys.End: Value = Maximum; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = g.DpiX / 96f, left = 12 * s, right = Width - left, y = Height / 2f;
        float x = left + (right - left) * Value / 100f, radius = 6 * s;
        Color filled = dark ? Color.FromArgb(229, 229, 231) : Color.FromArgb(85, 86, 90);
        Color remaining = dark ? Color.FromArgb(62, 63, 67) : Color.FromArgb(218, 219, 221);
        Color thumb = dark ? Color.FromArgb(65, 66, 71) : Color.White;
        Color rim = dark ? Color.FromArgb(183, 184, 188) : Color.FromArgb(210, 211, 214);
        if (!Enabled)
        {
            filled = Blend(filled); remaining = Blend(remaining);
            thumb = Blend(thumb); rim = Blend(rim);
        }
        using (var rail = new Pen(remaining, 3 * s))
        using (var progress = new Pen(filled, 3 * s))
        {
            rail.StartCap = rail.EndCap = progress.StartCap = progress.EndCap = LineCap.Round;
            g.DrawLine(rail, left, y, right, y);
            if (Value > 0) g.DrawLine(progress, left, y, x, y);
        }
        using (var shadow = new SolidBrush(Color.FromArgb(Enabled ? 22 : 8, Color.Black)))
            g.FillEllipse(shadow, x - radius - s, y - radius, 2 * radius + 2 * s, 2 * radius + 2 * s);
        using (var fill = new SolidBrush(thumb))
        using (var edge = new Pen(rim, s))
        {
            g.FillEllipse(fill, x - radius, y - radius, 2 * radius, 2 * radius);
            g.DrawEllipse(edge, x - radius, y - radius, 2 * radius, 2 * radius);
        }
        if (Focused && ShowFocusCues && Enabled)
            using (var focus = new Pen(rim, s))
                g.DrawEllipse(focus, x - radius - 3 * s, y - radius - 3 * s, 2 * radius + 6 * s, 2 * radius + 6 * s);
    }
    Color Blend(Color c)
    {
        return Color.FromArgb((c.R + 2 * BackColor.R) / 3, (c.G + 2 * BackColor.G) / 3, (c.B + 2 * BackColor.B) / 3);
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
    sealed class SliderAccessibility : ControlAccessibleObject
    {
        readonly OpacitySlider slider;
        internal SliderAccessibility(OpacitySlider slider) : base(slider) { this.slider = slider; }
        public override string Value
        {
            get { return slider.Value + "%"; }
            set { int percent; if (slider.Enabled && int.TryParse(value.TrimEnd('%'), out percent)) slider.Value = percent; }
        }
    }
}
