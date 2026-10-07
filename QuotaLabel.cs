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

class QuotaLabel : Form {
    readonly string home;
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly PetPanelSpring snap = new PetPanelSpring();
    PetPanelPlacement placement;
    PetState petState = PetState.Empty;
    Point anchor { get { return petState.Anchor; } }
    IntPtr target { get { return petState.Target; } }
    int petSize { get { return petState.Size; } }
    bool anchorKnown { get { return petState.AnchorKnown; } }
    bool petOpen { get { return petState.Visible; } }
    bool closing;
    Color panelColor = Color.FromArgb(230,27,31,38);
    int opacityPercent = 90;
    GlassBackdrop glass;
    CloseBubbleButton dismissButton;
    readonly ToolStripMenuItem showBubble;
    readonly ToolStripMenuItem opacityMenu;
    readonly bool diagnosticMode;
    bool bubbleRequested = true;
    Point previewPosition;
    readonly List<ToolStripMenuItem> opacityChoices = new List<ToolStripMenuItem>();
    readonly ToolTip refreshTip = new ToolTip();
    internal event Action RefreshRequested;
    string AppearancePath { get { return Path.Combine(Program.Folder,"appearance.json"); } }
    string line1 = "Reading quota...", line2 = "", status = "";
    string value1 = "", value2 = "";
    Point lastLabel = new Point(int.MinValue, int.MinValue), lastAnchor;
    internal ToolStripItem[] Commands { get { return new ToolStripItem[] { opacityMenu, showBubble }; } }
    public QuotaLabel(string data, bool diagnostic=false) {
        home = data; diagnosticMode=diagnostic;
        Text = "Quota Bubble"; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; BackColor = Color.FromArgb(27, 31, 38);
        ForeColor = Color.FromArgb(232, 237, 244); Font = new Font("Segoe UI", 9);
        ClientSize = new Size(190, 96); DoubleBuffered = true; Opacity = 1;
        Cursor=Cursors.Hand;
        refreshTip.SetToolTip(this,"Click to refresh quota; drag to reposition");
        string preferredCorner="top-end";
        try {
            if(File.Exists(AppearancePath)) {
                var settings=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(AppearancePath));
                object value;if(settings.TryGetValue("opacityPercent",out value))opacityPercent=Math.Max(10,Math.Min(100,Convert.ToInt32(value)));
                if(settings.TryGetValue("panelCorner",out value) && PetPanelPlacement.Valid(value as string))preferredCorner=(string)value;
            }
        }catch(Exception e) { Program.Record("appearance-error",new { error=e.GetType().Name }); }
        placement=new PetPanelPlacement(preferredCorner);
        panelColor=WithOpacity(panelColor);
        opacityMenu=new ToolStripMenuItem("Opacity");
        for(int percent=10;percent<=100;percent+=10) {
            var item=new ToolStripMenuItem(percent+"%");item.Tag=percent;
            item.Checked=percent==opacityPercent;
            item.Click+=(s,e)=>SetOpacity((int)((ToolStripMenuItem)s).Tag);
            opacityChoices.Add(item);opacityMenu.DropDownItems.Add(item);
        }
        showBubble=new ToolStripMenuItem("Show Quota Bubble",null,(s,e)=>SetBubbleVisible(true));
        showBubble.Enabled=false;
        Shown += (s,e) => RenderFrame();
    }
    protected override void Dispose(bool disposing) {
        if(disposing && !closing) {
            closing=true;
            placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null){dismissButton.Close();dismissButton.Dispose();dismissButton=null;}
            Owner=null;if(glass!=null){glass.Close();glass.Dispose();glass=null;}
            opacityMenu.Dispose();showBubble.Dispose();
            refreshTip.Dispose();RefreshRequested=null;
        }
        base.Dispose(disposing);
    }
    internal void UpdatePet(PetState state) {
        if(closing)return;
        if(state.Target!=petState.Target){placement.Cancel();snap.Cancel();Capture=false;}
        petState=state;
        if(state.Colour.HasValue) {
            var next=WithOpacity(state.Colour.Value);
            if(next!=panelColor){panelColor=next;RenderFrame();}
        }
        Follow();
    }
    internal void Present(QuotaState state) {
        if(closing)return;
        if(state.Snapshot!=null) {
            line1=WindowText(state.Snapshot.Primary,"Primary");
            line2=WindowText(state.Snapshot.Secondary,"Secondary");
            value1=WindowValue(state.Snapshot.Primary);value2=WindowValue(state.Snapshot.Secondary);
            status=(state.IsStale?"Failed · last ":"updated ")+state.Snapshot.CheckedAt.ToLocalTime().ToString("HH:mm");
        }else if(state.Error!=null) {
            line1="Unable to read CLI quota";line2="Click to retry";status="Quota unavailable";
            value1=value2="";
        }
        if(state.IsRefreshing)status="Updating...";
        RenderFrame();
    }
    static string WindowText(QuotaWindow window,string fallback) {
        if(window==null)return fallback+": unavailable";
        int mins=window.DurationMinutes;
        string name=mins==10080?"Week":mins%60==0 && mins>0?(mins/60)+"h":mins>0?mins+"m":fallback;
        return name;
    }
    static string WindowValue(QuotaWindow window) {return window==null?"":window.RemainingPercent.ToString("0.#")+"%";}
    protected override bool ShowWithoutActivation { get { return true; } }
    internal void PreviewGlass(Color colour,Point position) {
        previewPosition=position;
        panelColor=colour;line1="5h";line2="Week";value1="84%";value2="79%";status="updated 17:53";
        if(glass==null || !glass.Ready)throw new InvalidOperationException("Composition backdrop unavailable");
        glass.Follow(position,true);
        Native.SetWindowPos(Handle,new IntPtr(-1),position.X,position.Y,0,0,0x0010|0x0001);
        RenderFrame();
        if(dismissButton!=null)dismissButton.Follow(position,false);
    }
    internal CloseBubbleButton DismissButton {get{return dismissButton;}}
    internal ToolStripMenuItem ShowBubbleCommand {get{return showBubble;}}
    internal bool BubbleRequested {get{return bubbleRequested;}}
    internal void PreviewHover(Point pointer) {UpdateDismissButton(Location,pointer);}
    void UpdateDismissButton(Point position,Point pointer) {
        if(dismissButton==null)return;
        bool hover=Visible && bubbleRequested && (dismissButton.Capture ||
            CloseBubbleButton.Hovered(pointer,new Rectangle(position,Size),dismissButton.Visible));
        dismissButton.Follow(position,hover);
    }
    internal void SetBubbleVisible(bool requested) {
        bubbleRequested=requested;showBubble.Enabled=!requested;
        if(!requested) {
            placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null)dismissButton.Follow(Point.Empty,false);
            Hide();if(glass!=null)glass.Follow(Point.Empty,false);
        }else if(diagnosticMode) {Show();PreviewGlass(panelColor,previewPosition);}
        else {Follow();RenderFrame();}
        Program.Record("bubble-visibility",new {requested=requested,processContinues=!closing});
    }
    protected override void OnVisibleChanged(EventArgs e) {
        base.OnVisibleChanged(e);
        if(!Visible && dismissButton!=null)dismissButton.Follow(Point.Empty,false);
    }
    Color WithOpacity(Color c) {return Color.FromArgb((int)Math.Round(255*opacityPercent/100.0),c.R,c.G,c.B);}
    void SetOpacity(int percent) {
        opacityPercent=percent;panelColor=WithOpacity(panelColor);
        foreach(var item in opacityChoices)item.Checked=(int)item.Tag==percent;
        SaveAppearance();
        RenderFrame();
        Program.Record("opacity-changed",new {percent=opacityPercent,alpha=panelColor.A});
    }
    void SaveAppearance() {
        try {File.WriteAllText(AppearancePath,new JavaScriptSerializer().Serialize(new {
            opacityPercent=opacityPercent,panelCorner=placement.Preferred }));}
        catch(IOException e) {Program.Record("appearance-error",new { error=e.GetType().Name });}
    }
    protected override CreateParams CreateParams {
        get { var p=base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; return p; }
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE, keep editor focus.
        base.WndProc(ref m);
    }
    Rectangle PetBounds {get{return new Rectangle(anchor.X,anchor.Y,petSize,(int)Math.Ceiling(petSize*208.0/192));}}
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left || closing || !Visible || !bubbleRequested || !anchorKnown || !petOpen || target==IntPtr.Zero)return;
        snap.Cancel();placement.Begin(PointToScreen(e.Location),Location);
        Capture=true;Cursor=Cursors.SizeAll;
        Program.Record("panel-drag-start",new { corner=placement.Preferred });
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        Native.POINT hover;if(Native.GetCursorPos(out hover))UpdateDismissButton(Location,new Point(hover.X,hover.Y));
        if(!placement.Active)return;
        placement.Move(PointToScreen(e.Location));
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if(e.Button==MouseButtons.Left && placement.Active)
            FinishPanelDrag((Native.GetAsyncKeyState(27)&0x8000)!=0,PointToScreen(e.Location));
    }
    protected override void OnMouseCaptureChanged(EventArgs e) {
        base.OnMouseCaptureChanged(e);
        if(placement!=null && placement.Active && !Capture)FinishPanelDrag(true);
    }
    void FinishPanelDrag(bool cancel,Point? releasedAt=null) {
        if(!placement.Active)return;
        Native.POINT p;Point pointer=releasedAt ?? (Native.GetCursorPos(out p)?new Point(p.X,p.Y):Location);
        placement.Move(pointer);
        bool moved=placement.HasMoved,committed=false;
        Point from=placement.DragPosition;
        if(cancel)placement.Cancel();else committed=placement.End(pointer,PetBounds);
        Capture=false;Cursor=Cursors.Hand;
        if(moved)snap.Begin(new Point(from.X-anchor.X,from.Y-anchor.Y),watch.ElapsedMilliseconds);
        if(committed)SaveAppearance();
        Program.Record("panel-drag-end",new { cancelled=cancel,moved=moved,corner=placement.Preferred });
        // Only a real mouse-up inside the rounded body is a refresh. Timer-based
        // release recovery, capture loss and any gesture that crossed 4px are not.
        if(!cancel && !moved && releasedAt.HasValue && bubbleRequested && Visible) {
            using(var body=Rounded(new Rectangle(0,0,Width,Height),32))
                if(body.IsVisible(PointToClient(pointer))) {
                    var handler=RefreshRequested;if(handler!=null)handler();
                }
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try {
            glass=new GlassBackdrop(home);var handle=glass.Handle;
            if(glass.Ready)Owner=glass;
            else {glass.Dispose();glass=null;}
        }catch(Exception ex) {
            if(glass!=null){glass.Dispose();glass=null;}
            Program.Record("backdrop-blur",new {enabled=false,reason=ex.GetType().Name});
        }
        dismissButton=new CloseBubbleButton(()=>SetBubbleVisible(false));
        dismissButton.Owner=this;dismissButton.SetColour(panelColor);
    }
    protected override void OnPaint(PaintEventArgs e) { RenderFrame(); }
    void RenderFrame() {
        if(!IsHandleCreated || closing)return;
        if(glass!=null && glass.Ready)glass.SetTint(panelColor);
        if(dismissButton!=null)dismissButton.SetColour(panelColor);
        using(var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                // Tint and blur share the compositor clip; only use the GDI shape for fallback.
                if(glass==null || !glass.Ready)
                    using(var shape=Rounded(new Rectangle(0,0,Width,Height),32))
                    using(var fill=new SolidBrush(panelColor))g.FillPath(fill,shape);
                else
                    // A layered HWND with alpha=0 passes input through. Minimum alpha
                    // makes the entire rounded body draggable, including gaps between
                    // text, while the visible tint stays in the Composition layer.
                    using(var shape=Rounded(new Rectangle(0,0,Width,Height),32))
                    using(var hit=new SolidBrush(Color.FromArgb(1,panelColor.R,panelColor.G,panelColor.B)))g.FillPath(hit,shape);
                Color fg=PetTheme.Text(panelColor),muted=PetTheme.Secondary(panelColor);
                // Inset the one-pixel stroke so antialiasing stays inside the HWND.
                using(var outline=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),31.5f))
                using(var pen=new Pen(PetTheme.Outline(panelColor),1f))g.DrawPath(pen,outline);
                using(var title=new Font("Segoe UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var detail=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
                // Match the native Status Bubble title: 13px, weight 700, 16px line height.
                using(var amount=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var primary=new SolidBrush(fg))
                using(var secondary=new SolidBrush(muted)) {
                    g.DrawString("Quota remaining",title,primary,24,12,StringFormat.GenericTypographic);
                    DrawQuotaRow(g,amount,primary,line1,value1,34);
                    DrawQuotaRow(g,amount,primary,line2,value2,54);
                    g.DrawString(status,detail,secondary,24,76,StringFormat.GenericTypographic);
                }
            }
            var screen=Native.GetDC(IntPtr.Zero);var dc=Native.CreateCompatibleDC(screen);
            var handle=bitmap.GetHbitmap(Color.FromArgb(0));var previous=Native.SelectObject(dc,handle);
            try {
                var position=new Native.POINT { X=Left,Y=Top };var origin=new Native.POINT();
                var size=new Native.SIZE { Width=Width,Height=Height };
                var blend=new Native.BLEND { SourceConstantAlpha=255,AlphaFormat=1 };
                if(!Native.UpdateLayeredWindow(Handle,screen,ref position,ref size,dc,ref origin,0,ref blend,2))
                    Program.Record("render-error",new { error=Marshal.GetLastWin32Error() });
            }finally {Native.SelectObject(dc,previous);Native.DeleteObject(handle);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
        }
    }
    static void DrawQuotaRow(Graphics g,Font font,Brush ink,string label,string value,float top) {
        using(var left=(StringFormat)StringFormat.GenericTypographic.Clone())
        using(var right=(StringFormat)StringFormat.GenericTypographic.Clone()) {
            left.FormatFlags|=StringFormatFlags.NoWrap;left.Trimming=StringTrimming.EllipsisCharacter;
            right.Alignment=StringAlignment.Far;right.FormatFlags|=StringFormatFlags.NoWrap;
            float valueWidth=value.Length==0?0:g.MeasureString(value,font,PointF.Empty,StringFormat.GenericTypographic).Width;
            g.DrawString(label,font,ink,new RectangleF(24,top,142-(valueWidth>0?valueWidth+12:0),18),left);
            if(value.Length>0)g.DrawString(value,font,ink,new RectangleF(24,top,142,18),right);
        }
    }
    static GraphicsPath Rounded(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    void Follow() {
        if(closing || !IsHandleCreated)return;
        if(!petState.Visible || !bubbleRequested) {
            placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null)dismissButton.Follow(Point.Empty,false);
            if(Visible)Hide();if(glass!=null)glass.Follow(Point.Empty,false);return;
        }
        if(!Visible)Show();
        var monitor=Screen.FromPoint(anchor).WorkingArea;
        int edgePadding=glass!=null?24:0;
        var desired=placement.Resolve(PetBounds,Size,monitor,edgePadding);
        if(placement.Active) {
            Native.POINT pointer;
            if(Native.GetCursorPos(out pointer))placement.Move(new Point(pointer.X,pointer.Y));
            int button=Native.GetSystemMetrics(23)!=0?2:1;
            if((Native.GetAsyncKeyState(27)&0x8000)!=0)FinishPanelDrag(true);
            else if((Native.GetAsyncKeyState(button)&0x8000)==0)FinishPanelDrag(false);
            if(placement.Active)desired=placement.DragPosition;
            else desired=placement.Resolve(PetBounds,Size,monitor,edgePadding);
        }
        if(!placement.Active && snap.Active) {
            var offset=snap.Step(new Point(desired.X-anchor.X,desired.Y-anchor.Y),watch.ElapsedMilliseconds);
            desired=PetPanelPlacement.Constrain(new Point(anchor.X+offset.X,anchor.Y+offset.Y),Size,monitor,edgePadding);
        }
        int x=desired.X,y=desired.Y;
        if(glass!=null)glass.Follow(desired,true);
        if(desired!=lastLabel) {
            Native.SetWindowPos(Handle,new IntPtr(-1),x,y,0,0,0x0010|0x0001);
            Native.RECT actual;Native.GetWindowRect(Handle,out actual);
            Program.Record("placement",new { targetHwnd=target.ToInt64(), anchorX=anchor.X,anchorY=anchor.Y,
                labelX=actual.Left,labelY=actual.Top, source=petState.Dragging?"drag-pointer-anchor":"saved-anchor-or-release-reconciliation", milliseconds=watch.ElapsedMilliseconds,
                panelDragging=placement.Active,corner=placement.Effective,preferredCorner=placement.Preferred,
                deltaX=anchorKnown?anchor.X-lastAnchor.X:0, deltaY=anchorKnown?anchor.Y-lastAnchor.Y:0 });
            lastAnchor=anchor;lastLabel=desired;
        }
        Native.POINT hover;if(Native.GetCursorPos(out hover))UpdateDismissButton(desired,new Point(hover.X,hover.Y));
    }
}
