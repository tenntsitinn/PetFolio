using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A small owned, nonactivating window lets the button protrude beyond the bubble
// without resizing its content, glass mask, drag geometry or shadow.
sealed class CloseBubbleButton : Form {
    internal const int Diameter=20, OffsetX=-4, OffsetY=-5;
    readonly Action dismiss;
    readonly ToolTip tooltip=new ToolTip();
    Color colour;
    bool pressed;
    internal CloseBubbleButton(Action handler) {
        dismiss=handler;Text="Hide Quota Bubble";FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        ClientSize=new Size(Diameter,Diameter);Cursor=Cursors.Hand;
        AccessibleName="Hide Quota Bubble";AccessibleRole=AccessibleRole.PushButton;
        tooltip.SetToolTip(this,"Hide Quota Bubble");
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
        get {var p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x80000;return p;}
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21){m.Result=new IntPtr(3);return;}
        base.WndProc(ref m);
    }
    internal void SetColour(Color value) {
        if(colour==value)return;colour=value;if(IsHandleCreated)Render();
    }
    internal void Follow(Point bubble,bool visible) {
        if(!visible){pressed=false;Capture=false;if(Visible)Hide();return;}
        var position=new Point(bubble.X+OffsetX,bubble.Y+OffsetY);
        if(Location!=position || !Visible)Native.SetWindowPos(Handle,new IntPtr(-1),position.X,position.Y,0,0,0x0010|0x0001);
        if(!Visible)Show();
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Left && Inside(e.Location)){pressed=true;Capture=true;}
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if(e.Button!=MouseButtons.Left)return;
        bool click=pressed && Inside(e.Location);pressed=false;Capture=false;
        if(click)dismiss();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) {
        base.OnMouseCaptureChanged(e);if(!Capture)pressed=false;
    }
    static bool Inside(Point p) {double x=p.X-10,y=p.Y-10;return x*x+y*y<=100;}
    internal static bool Hovered(Point pointer,Rectangle bubble,bool buttonVisible) {
        if(bubble.Contains(pointer)) {
            double radius=Math.Min(32,Math.Min(bubble.Width,bubble.Height)/2.0);
            double dx=Math.Max(0,Math.Abs(pointer.X-bubble.Left-bubble.Width/2.0)-(bubble.Width/2.0-radius));
            double dy=Math.Max(0,Math.Abs(pointer.Y-bubble.Top-bubble.Height/2.0)-(bubble.Height/2.0-radius));
            if(dx*dx+dy*dy<=radius*radius)return true;
        }
        // The protruding button is part of the hover region once revealed.
        return buttonVisible && Inside(new Point(pointer.X-bubble.X-OffsetX,pointer.Y-bubble.Y-OffsetY));
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {Render();}
    protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);Render();}
    internal static Bitmap Frame(Color fill) {
        var frame=new Bitmap(Diameter,Diameter,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(frame)) {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var brush=new SolidBrush(fill))g.FillEllipse(brush,0,0,Diameter,Diameter);
            using(var outline=new Pen(PetTheme.Outline(fill),1f))
                g.DrawEllipse(outline,.5f,.5f,Diameter-1,Diameter-1);
            Color text=PetTheme.Text(fill);
            using(var pen=new Pen(text,1.5f)) {
                pen.StartCap=pen.EndCap=LineCap.Round;
                g.DrawLine(pen,6.5f,6.5f,13.5f,13.5f);g.DrawLine(pen,13.5f,6.5f,6.5f,13.5f);
            }
        }
        return frame;
    }
    void Render() {
        if(!IsHandleCreated || IsDisposed)return;
        using(var bitmap=Frame(colour)) {
            var screen=Native.GetDC(IntPtr.Zero);var dc=Native.CreateCompatibleDC(screen);
            var h=bitmap.GetHbitmap(Color.FromArgb(0));var previous=Native.SelectObject(dc,h);
            try {
                var pos=new Native.POINT {X=Left,Y=Top};var origin=new Native.POINT();
                var size=new Native.SIZE {Width=Diameter,Height=Diameter};
                var blend=new Native.BLEND {SourceConstantAlpha=255,AlphaFormat=1};
                if(!Native.UpdateLayeredWindow(Handle,screen,ref pos,ref size,dc,ref origin,0,ref blend,2))
                    Program.Record("close-button-render-error",new {error=Marshal.GetLastWin32Error()});
            }finally {Native.SelectObject(dc,previous);Native.DeleteObject(h);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
        }
    }
    protected override void Dispose(bool disposing) {if(disposing)tooltip.Dispose();base.Dispose(disposing);}
}
