using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Presentation only: existing ToolStrip items retain their events and state.
sealed class TrayMenuStyle : IDisposable {
    readonly HashSet<ToolStripDropDown> menus=new HashSet<ToolStripDropDown>();
    readonly Font font=(Font)SystemFonts.MenuFont.Clone();
    readonly Renderer renderer=new Renderer();
    readonly Timer themeTimer=new Timer();
    internal Func<bool?> ReadTheme;
    internal bool Dark {get{return renderer.Dark;}}
    public TrayMenuStyle(string home=null) {
        ReadTheme=()=>CodexMenuTheme.Read(home,BubbleAppearance.ReadSystemDark);
        renderer.Dark=ReadTheme() ?? false;
        themeTimer.Interval=2000;themeTimer.Tick+=(s,e)=>RefreshTheme();themeTimer.Start();
    }
    internal void RefreshTheme() {
        var dark=ReadTheme();if(!dark.HasValue || dark.Value==renderer.Dark)return;
        renderer.Dark=dark.Value;
        foreach(var menu in menus)if(!menu.IsDisposed)Colour(menu);
    }
    void Colour(ToolStripDropDown menu) {
        menu.BackColor=renderer.Surface;menu.ForeColor=renderer.Text;
        foreach(ToolStripItem item in menu.Items)item.ForeColor=renderer.Text;
        menu.Invalidate(true);
    }
    public void Apply(ToolStripDropDown menu) {
        if(menus.Add(menu)) {
            menu.Font=font;menu.Renderer=renderer;
            menu.Opening+=(s,e)=>{RefreshTheme();Layout(menu);};
            menu.SizeChanged+=(s,e)=>RoundWindow(menu);
            menu.Opened+=(s,e)=>{Layout(menu);RoundWindow(menu);};
        }
        Layout(menu);
        Colour(menu);
        foreach(ToolStripItem item in menu.Items) {
            var child=item as ToolStripMenuItem;
            if(child!=null && child.HasDropDownItems)Apply(child.DropDown);
        }
    }
    static float Scale(ToolStrip menu) {
        if(menu.IsHandleCreated) {
            try {return Math.Max(1,GetDpiForWindow(menu.Handle)/96f);}
            catch(EntryPointNotFoundException) { }
        }
        using(var g=menu.CreateGraphics())return Math.Max(1,g.DpiX/96f);
    }
    void Layout(ToolStripDropDown menu) {
        float scale=Scale(menu);int inset=(int)Math.Round(4*scale),width=(int)Math.Round(208*scale);
        menu.Padding=new Padding(inset);menu.Font=font;
        foreach(ToolStripItem item in menu.Items) {
            if(!item.Available || item is ToolStripSeparator)continue;
            int measured=TextRenderer.MeasureText(item.Text,font,Size.Empty,TextFormatFlags.SingleLine).Width;
            width=Math.Max(width,measured+(int)Math.Round(60*scale));
        }
        foreach(ToolStripItem item in menu.Items) {
            item.Font=font;item.AutoSize=false;item.Margin=Padding.Empty;
            item.Size=new Size(width,(int)Math.Round((item is ToolStripSeparator?9:28)*scale));
        }
        RoundWindow(menu);
    }
    static void RoundWindow(ToolStripDropDown menu) {
        if(menu.Width<=0 || menu.Height<=0)return;
        using(var path=Rounded(new RectangleF(0,0,menu.Width,menu.Height),8*Scale(menu))) {
            var old=menu.Region;menu.Region=new Region(path);if(old!=null)old.Dispose();
        }
    }
    static GraphicsPath Rounded(RectangleF r,float radius) {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var p=new GraphicsPath();
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    sealed class Renderer : ToolStripProfessionalRenderer {
        internal bool Dark;
        internal Color Surface {get{return Dark?Color.FromArgb(40,40,40):Color.White;}}
        internal Color Text {get{return Dark?Color.FromArgb(245,245,245):Color.FromArgb(13,13,13);}}
        Color Disabled {get{return Color.FromArgb(143,143,143);}}
        Color Edge {get{return Dark?Color.FromArgb(68,68,68):Color.FromArgb(216,216,216);}}
        Color Hover {get{return Dark?Color.FromArgb(62,62,62):Color.FromArgb(240,240,240);}}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            e.Graphics.Clear(Surface);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Rounded(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),8*Scale(e.ToolStrip)))
            using(var pen=new Pen(Edge))e.Graphics.DrawPath(pen,path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if(!e.Item.Selected || !e.Item.Enabled)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Rounded(new RectangleF(0,0,e.Item.Width,e.Item.Height),4*Scale(e.ToolStrip)))
            using(var brush=new SolidBrush(Hover))e.Graphics.FillPath(brush,path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            float scale=Scale(e.ToolStrip);int left=(int)Math.Round(28*scale),right=(int)Math.Round(24*scale);
            e.TextRectangle=new Rectangle(left,0,Math.Max(1,e.Item.Width-left-right),e.Item.Height);
            e.TextFormat=TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.SingleLine;
            e.TextColor=e.Item.Enabled?Text:Disabled;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            float scale=Scale(e.Item.Owner),x=e.Item.Width-12*scale,y=e.Item.Height/2f;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(e.Item.Enabled?Text:Disabled,1.2f*scale)) {
                e.Graphics.DrawLines(pen,new[]{new PointF(x-2*scale,y-3*scale),new PointF(x+1*scale,y),new PointF(x-2*scale,y+3*scale)});
            }
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            float scale=Scale(e.ToolStrip),x=13*scale,y=e.Item.Height/2f;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(e.Item.Enabled?Text:Disabled,1.5f*scale)) {
                pen.StartCap=pen.EndCap=LineCap.Round;
                e.Graphics.DrawLines(pen,new[]{new PointF(x-4*scale,y),new PointF(x-1*scale,y+3*scale),new PointF(x+5*scale,y-4*scale)});
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int inset=(int)Math.Round(8*Scale(e.ToolStrip));
            using(var pen=new Pen(Edge))e.Graphics.DrawLine(pen,inset,e.Item.Height/2,e.Item.Width-inset,e.Item.Height/2);
        }
    }
    public void Dispose() {themeTimer.Dispose();menus.Clear();font.Dispose();}
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
}
