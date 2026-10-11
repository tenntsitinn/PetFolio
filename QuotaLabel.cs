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
    bool trayMenuOpen;
    Color panelColor = Color.FromArgb(230,27,31,38);
    int opacityPercent = 90;
    Color petColour=Color.FromArgb(27,31,38);
    internal bool DefaultAppearance {get;private set;}
    bool defaultFrostedGlass,petFrostedGlass=true;
    internal bool FrostedGlass {get{return DefaultAppearance?defaultFrostedGlass:petFrostedGlass;}}
    bool systemDark;
    internal Func<bool?> ReadSystemDark=BubbleAppearance.ReadSystemDark;
    readonly System.Windows.Forms.Timer appearanceTimer=new System.Windows.Forms.Timer();
    Color? desktopBackground;
    internal Func<Rectangle,Color,Color?> ReadBackground=BubbleBackdrop.Capture;
    internal bool UsesBlackText {get{return PetTheme.Text(panelColor,desktopBackground).ToArgb()==Color.Black.ToArgb();}}
    GlassBackdrop glass;
    CloseBubbleButton dismissButton;
    readonly ToolStripMenuItem showBubble;
    readonly ToolStripMenuItem opacityMenu;
    readonly ToolStripMenuItem appearanceMenu;
    readonly ToolStripMenuItem defaultAppearance;
    readonly ToolStripMenuItem petAppearance;
    readonly ToolStripMenuItem frostedGlass;
    readonly ToolStripMenuItem modeMenu;
    readonly System.Windows.Forms.Timer clickTimer = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer systemTimer = new System.Windows.Forms.Timer();
    SystemMetricsSource systemSource;
    SystemMetrics systemMetrics = new SystemMetrics();
    double? quotaPercent1,quotaPercent2;
    internal bool SystemMode {get;private set;}
    internal bool QuotaEnabled {get;private set;}
    internal bool SystemEnabled {get;private set;}
    internal bool AnyModeEnabled {get{return QuotaEnabled || SystemEnabled;}}
    readonly bool diagnosticMode;
    bool bubbleRequested = true;
    Point previewPosition;
    readonly List<ToolStripMenuItem> opacityChoices = new List<ToolStripMenuItem>();
    readonly ToolTip refreshTip = new ToolTip();
    internal event Action RefreshRequested;
    string AppearancePath { get { return Path.Combine(Program.DataFolder,"appearance.json"); } }
    string line1 = "Reading quota...", line2 = "", status = "";
    string value1 = "", value2 = "";
    Point lastLabel = new Point(int.MinValue, int.MinValue), lastAnchor;
    internal ToolStripItem[] Commands { get { return new ToolStripItem[] { appearanceMenu, showBubble, modeMenu }; } }
    public QuotaLabel(string data, bool diagnostic=false) {
        home = data; diagnosticMode=diagnostic;QuotaEnabled=SystemEnabled=true;DefaultAppearance=true;
        Text = "Quota Bubble"; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; BackColor = Color.FromArgb(27, 31, 38);
        ForeColor = Color.FromArgb(232, 237, 244); Font = new Font("Segoe UI", 9);
        ClientSize = new Size(BubbleLayout.Width,BubbleLayout.Height); DoubleBuffered = true; Opacity = 1;
        Cursor=Cursors.Hand;
        SetStyle(ControlStyles.StandardDoubleClick,true);
        clickTimer.Interval=SystemInformation.DoubleClickTime;
        clickTimer.Tick+=(s,e)=> {clickTimer.Stop();if(closing || !Visible || !bubbleRequested)return;
            RefreshCurrent();};
        systemTimer.Interval=2000;
        systemTimer.Tick+=(s,e)=>SampleSystem();
        string preferredCorner="top-end";
        try {
            if(File.Exists(AppearancePath)) {
                var settings=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(AppearancePath));
                object value;if(settings.TryGetValue("opacityPercent",out value))opacityPercent=Math.Max(10,Math.Min(100,Convert.ToInt32(value)));
                if(settings.TryGetValue("panelCorner",out value) && PetPanelPlacement.Valid(value as string))preferredCorner=(string)value;
                if(settings.TryGetValue("bubbleMode",out value))SystemMode=(value as string)=="system";
                if(settings.TryGetValue("quotaEnabled",out value))QuotaEnabled=Convert.ToBoolean(value);
                if(settings.TryGetValue("systemEnabled",out value))SystemEnabled=Convert.ToBoolean(value);
                if(settings.TryGetValue("appearanceMode",out value))DefaultAppearance=(value as string)!="pet";
                if(settings.TryGetValue("defaultFrostedGlass",out value))defaultFrostedGlass=Convert.ToBoolean(value);
                if(settings.TryGetValue("petFrostedGlass",out value))petFrostedGlass=Convert.ToBoolean(value);
            }
        }catch(Exception e) { Program.Record("appearance-error",new { error=e.GetType().Name }); }
        placement=new PetPanelPlacement(preferredCorner);
        if(SystemMode && !SystemEnabled && QuotaEnabled)SystemMode=false;
        if(!SystemMode && !QuotaEnabled && SystemEnabled)SystemMode=true;
        systemDark=ReadSystemDark() ?? false;
        panelColor=DefaultAppearance?BubbleAppearance.Surface(systemDark):WithOpacity(petColour);
        opacityMenu=new ToolStripMenuItem("Opacity");
        for(int percent=10;percent<=100;percent+=10) {
            var item=new ToolStripMenuItem(percent+"%");item.Tag=percent;
            item.Checked=percent==opacityPercent;
            item.Click+=(s,e)=>SetOpacity((int)((ToolStripMenuItem)s).Tag);
            opacityChoices.Add(item);opacityMenu.DropDownItems.Add(item);
        }
        appearanceMenu=new ToolStripMenuItem("Appearance");
        defaultAppearance=new ToolStripMenuItem("Default",null,(s,e)=>SetDefaultAppearance(true));
        petAppearance=new ToolStripMenuItem("Pet colour",null,(s,e)=>SetDefaultAppearance(false));
        frostedGlass=new ToolStripMenuItem("Frosted glass",null,(s,e)=>SetFrostedGlass(!FrostedGlass));
        appearanceMenu.DropDownItems.AddRange(new ToolStripItem[]{defaultAppearance,petAppearance,new ToolStripSeparator(),opacityMenu,frostedGlass});
        UpdateAppearanceMenu();
        appearanceTimer.Interval=2000;
        appearanceTimer.Tick+=(s,e)=>RefreshSystemAppearance();
        if(DefaultAppearance)appearanceTimer.Start();
        showBubble=new ToolStripMenuItem("Show Bubble",null,(s,e)=>SetBubbleVisible(true));
        showBubble.Enabled=false;
        modeMenu=new ToolStripMenuItem("Switch to System Bubble",null,(s,e)=>SetMode(!SystemMode));
        UpdateModeLabels();
        Shown += (s,e) => RenderFrame();
    }
    protected override void Dispose(bool disposing) {
        if(disposing && !closing) {
            closing=true;
            clickTimer.Dispose();systemTimer.Dispose();appearanceTimer.Dispose();systemSource=null;
            placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null){dismissButton.Close();dismissButton.Dispose();dismissButton=null;}
            Owner=null;if(glass!=null){glass.Close();glass.Dispose();glass=null;}
            appearanceMenu.Dispose();showBubble.Dispose();modeMenu.Dispose();
            refreshTip.Dispose();RefreshRequested=null;
        }
        base.Dispose(disposing);
    }
    internal void UpdatePet(PetState state) {
        if(closing)return;
        if(state.Target!=petState.Target){clickTimer.Stop();placement.Cancel();snap.Cancel();Capture=false;}
        petState=state;
        if(state.Colour.HasValue) {
            petColour=state.Colour.Value;
            var next=DefaultAppearance?BubbleAppearance.Surface(systemDark):WithOpacity(petColour);
            if(next!=panelColor){panelColor=next;RenderFrame();}
        }
        Follow();
    }
    internal void Present(QuotaState state) {
        if(closing)return;
        // IsRefreshing is emitted once per accepted refresh, before the CLI
        // starts. Timer, tray and bubble clicks all pass through this path.
        if(state.IsRefreshing)RefreshBackground();
        if(state.Snapshot!=null) {
            line1=WindowText(state.Snapshot.Primary,"Primary");
            line2=WindowText(state.Snapshot.Secondary,"Secondary");
            value1=WindowValue(state.Snapshot.Primary);value2=WindowValue(state.Snapshot.Secondary);
            quotaPercent1=state.Snapshot.Primary==null?(double?)null:state.Snapshot.Primary.RemainingPercent;
            quotaPercent2=state.Snapshot.Secondary==null?(double?)null:state.Snapshot.Secondary.RemainingPercent;
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
        panelColor=colour;line1="5h";line2="Week";value1="84%";value2="79%";quotaPercent1=84;quotaPercent2=79;status="updated 17:53";
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
    internal void SetTrayMenuOpen(bool open) {
        if(closing || trayMenuOpen==open)return;
        trayMenuOpen=open;
        if(open) {
            clickTimer.Stop();placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null)dismissButton.Follow(Point.Empty,false);
        }else if(!diagnosticMode)Follow();
    }
    void UpdateDismissButton(Point position,Point pointer) {
        if(dismissButton==null)return;
        bool hover=!trayMenuOpen && Visible && bubbleRequested && (dismissButton.Capture ||
            CloseBubbleButton.Hovered(pointer,new Rectangle(position,Size),dismissButton.Visible));
        dismissButton.Follow(position,hover);
    }
    internal void SetBubbleVisible(bool requested) {
        if(requested && !AnyModeEnabled)return;
        bubbleRequested=requested;showBubble.Enabled=!requested;
        if(!requested) {
            clickTimer.Stop();systemTimer.Stop();systemSource=null;
            placement.Cancel();snap.Cancel();Capture=false;
            if(dismissButton!=null)dismissButton.Follow(Point.Empty,false);
            Hide();if(glass!=null)glass.Follow(Point.Empty,false);
        }else if(diagnosticMode) {Show();PreviewGlass(panelColor,previewPosition);}
        else {Follow();RenderFrame();}
        Program.Record("bubble-visibility",new {requested=requested,processContinues=!closing});
    }
    protected override void OnVisibleChanged(EventArgs e) {
        base.OnVisibleChanged(e);
        if(!Visible){clickTimer.Stop();systemTimer.Stop();systemSource=null;}
        else if(SystemMode && SystemEnabled && !closing){SampleSystem();systemTimer.Start();}
        if(!Visible && dismissButton!=null)dismissButton.Follow(Point.Empty,false);
    }
    Color WithOpacity(Color c) {return Color.FromArgb((int)Math.Round(255*opacityPercent/100.0),c.R,c.G,c.B);}
    void UpdateAppearanceMenu() {
        defaultAppearance.Checked=DefaultAppearance;petAppearance.Checked=!DefaultAppearance;
        opacityMenu.Enabled=!DefaultAppearance;
        frostedGlass.Checked=FrostedGlass;
    }
    internal void SetFrostedGlass(bool enabled) {
        if(closing)return;
        if(DefaultAppearance)defaultFrostedGlass=enabled;else petFrostedGlass=enabled;
        desktopBackground=null;UpdateAppearanceMenu();SaveAppearance();RenderFrame();
        if(!DefaultAppearance){RefreshBackground();RenderFrame();}
    }
    internal void SetDefaultAppearance(bool useDefault) {
        if(closing)return;
        DefaultAppearance=useDefault;
        if(useDefault){systemDark=ReadSystemDark() ?? systemDark;appearanceTimer.Start();}
        else appearanceTimer.Stop();
        desktopBackground=null;
        panelColor=useDefault?BubbleAppearance.Surface(systemDark):WithOpacity(petColour);
        UpdateAppearanceMenu();SaveAppearance();RenderFrame();
        if(!useDefault){RefreshBackground();RenderFrame();}
    }
    internal void RefreshSystemAppearance() {
        if(closing || !DefaultAppearance)return;
        bool next=ReadSystemDark() ?? systemDark;
        if(next==systemDark)return;
        systemDark=next;panelColor=BubbleAppearance.Surface(systemDark);RenderFrame();
    }
    internal void SetMode(bool system) {
        if(closing || SystemMode==system || (system?!SystemEnabled:!QuotaEnabled))return;
        clickTimer.Stop();systemTimer.Stop();systemSource=null;
        SystemMode=system;UpdateModeLabels();SaveAppearance();
        if(SystemMode && Visible && bubbleRequested){SampleSystem();systemTimer.Start();}
        RenderFrame();
        Program.Record("bubble-mode",new {mode=SystemMode?"system":"quota"});
    }
    void UpdateModeLabels() {
        Text=SystemMode?"System usage":"Quota remaining";
        showBubble.Enabled=AnyModeEnabled && !bubbleRequested;
        modeMenu.Available=modeMenu.Enabled=QuotaEnabled && SystemEnabled;
        modeMenu.Text=SystemMode?"Switch to Quota remaining":"Switch to System usage";
        refreshTip.SetToolTip(this,QuotaEnabled && SystemEnabled?
            "Click to refresh; double-click to switch Quota / System; drag to reposition":"Click to refresh; drag to reposition");
    }
    internal void SetEnabledModes(bool quota,bool system) {
        if(closing)return;
        clickTimer.Stop();QuotaEnabled=quota;SystemEnabled=system;
        if(!system){systemTimer.Stop();systemSource=null;}
        if(SystemMode && !system && quota)SetMode(false);
        else if(!SystemMode && !quota && system)SetMode(true);
        UpdateModeLabels();SaveAppearance();Follow();RenderFrame();
    }
    internal void RefreshCurrent() {
        if(closing || !AnyModeEnabled)return;
        RefreshSystemAppearance();
        if(SystemMode)SampleSystem(true);
        else {var handler=RefreshRequested;if(handler!=null)handler();}
    }
    void SampleSystem(bool manual=false) {
        if(closing || !SystemMode || !SystemEnabled || (!manual && (!Visible || !bubbleRequested)))return;
        if(systemSource==null)systemSource=new SystemMetricsSource();
        try {systemMetrics=systemSource.Read();}
        catch(Exception e) {systemMetrics=new SystemMetrics();systemSource=null;Program.Record("system-metrics-error",new {error=e.GetType().Name});}
        // System refreshes independently of quota. Low-opacity ink must follow
        // the current desktop rather than wait for the five-minute quota timer.
        if(manual || opacityPercent==10 || opacityPercent==20)RefreshBackground();
        if(!Visible || !bubbleRequested)systemSource=null;
        RenderFrame();
    }
    void RefreshBackground() {
        if(closing || trayMenuOpen || DefaultAppearance || !Visible || !bubbleRequested)return;
        var background=ReadBackground(Bounds,panelColor);
        // Capture failure must not discard the last known background.
        if(background.HasValue)desktopBackground=background;
    }
    void SetOpacity(int percent) {
        if(DefaultAppearance)return;
        opacityPercent=percent;panelColor=WithOpacity(panelColor);
        foreach(var item in opacityChoices)item.Checked=(int)item.Tag==percent;
        SaveAppearance();
        // Commit the new tint before sampling: the sampler removes this alpha
        // from the visible glass. Movement and ordinary repaint never sample.
        RenderFrame();
        RefreshBackground();
        RenderFrame();
        Program.Record("opacity-changed",new {percent=opacityPercent,alpha=panelColor.A});
    }
    void SaveAppearance() {
        try {File.WriteAllText(AppearancePath,new JavaScriptSerializer().Serialize(new {
            opacityPercent=opacityPercent,panelCorner=placement.Preferred,bubbleMode=SystemMode?"system":"quota",
            quotaEnabled=QuotaEnabled,systemEnabled=SystemEnabled,appearanceMode=DefaultAppearance?"default":"pet",
            defaultFrostedGlass=defaultFrostedGlass,petFrostedGlass=petFrostedGlass }));}
        catch(IOException e) {Program.Record("appearance-error",new { error=e.GetType().Name });}
    }
    protected override CreateParams CreateParams {
        get { var p=base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; return p; }
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE, keep editor focus.
        base.WndProc(ref m);
        if(m.Msg==0x1A || m.Msg==0x31A)RefreshSystemAppearance(); // Settings / theme changed.
    }
    Rectangle PetBounds {get{return new Rectangle(anchor.X,anchor.Y,petSize,(int)Math.Ceiling(petSize*208.0/192));}}
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left || closing || trayMenuOpen || !Visible || !bubbleRequested || !anchorKnown || !petOpen || target==IntPtr.Zero)return;
        if(e.Clicks>=2 && clickTimer.Enabled && QuotaEnabled && SystemEnabled) {
            clickTimer.Stop();placement.Cancel();Capture=false;Cursor=Cursors.Hand;SetMode(!SystemMode);return;
        }
        snap.Cancel();placement.Begin(PointToScreen(e.Location),Location);
        Capture=true;Cursor=Cursors.SizeAll;
        Program.Record("panel-drag-start",new { corner=placement.Preferred });
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        Native.POINT hover;if(Native.GetCursorPos(out hover))UpdateDismissButton(Location,new Point(hover.X,hover.Y));
        if(!placement.Active)return;
        placement.Move(PointToScreen(e.Location));
        if(placement.HasMoved)clickTimer.Stop();
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
            using(var body=Rounded(new Rectangle(0,0,Width,Height),BubbleLayout.Radius))
                if(body.IsVisible(PointToClient(pointer))) {
                    clickTimer.Stop();clickTimer.Start();
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
        if(glass!=null && glass.Ready){glass.SetNativeAppearance(DefaultAppearance);glass.SetBlurEnabled(FrostedGlass);glass.SetTint(panelColor);}
        if(dismissButton!=null)dismissButton.SetColour(panelColor,DefaultAppearance,systemDark);
        using(var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                // Tint and blur share the compositor clip; only use the GDI shape for fallback.
                if(glass==null || !glass.Ready)
                    using(var shape=Rounded(new Rectangle(0,0,Width,Height),BubbleLayout.Radius))
                    using(var fill=new SolidBrush(panelColor))g.FillPath(fill,shape);
                else
                    // A layered HWND with alpha=0 passes input through. Minimum alpha
                    // makes the entire rounded body draggable, including gaps between
                    // text, while the visible tint stays in the Composition layer.
                    using(var shape=Rounded(new Rectangle(0,0,Width,Height),BubbleLayout.Radius))
                    using(var hit=new SolidBrush(Color.FromArgb(1,panelColor.R,panelColor.G,panelColor.B)))g.FillPath(hit,shape);
                Color fg=DefaultAppearance?BubbleAppearance.Text(systemDark):PetTheme.Text(panelColor,desktopBackground);
                Color muted=DefaultAppearance?BubbleAppearance.Secondary(systemDark):PetTheme.Secondary(panelColor,desktopBackground);
                // Inset the one-pixel stroke so antialiasing stays inside the HWND.
                using(var outline=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),BubbleLayout.Radius-.5f))
                if(DefaultAppearance)BubbleAppearance.DrawEdge(g,outline,new RectangleF(0,0,Width,Height),systemDark);
                else using(var pen=new Pen(PetTheme.Outline(panelColor),1f))g.DrawPath(pen,outline);
                using(var title=new Font("Segoe UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var detail=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var amount=new Font("Segoe UI",16,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var primary=new SolidBrush(fg))
                using(var secondary=new SolidBrush(muted)) {
                    DrawText(g,title,DefaultAppearance?primary:secondary,SystemMode?"System usage":"Quota remaining",20,12,168);
                    if(SystemMode) {
                        DrawMetric(g,detail,amount,primary,secondary,"CPU",SystemMetrics.Percent(systemMetrics.CpuPercent),systemMetrics.CpuPercent,20);
                        DrawMetric(g,detail,amount,primary,secondary,"RAM",SystemMetrics.Percent(systemMetrics.MemoryPercent),systemMetrics.MemoryPercent,114);
                        string network="↓ "+SystemMetrics.Rate(systemMetrics.DownloadBytesPerSecond)+"    ↑ "+SystemMetrics.Rate(systemMetrics.UploadBytesPerSecond);
                        if(g.MeasureString(network,detail,PointF.Empty,StringFormat.GenericTypographic).Width>168)
                            network="↓ "+SystemMetrics.Rate(systemMetrics.DownloadBytesPerSecond,true)+"    ↑ "+SystemMetrics.Rate(systemMetrics.UploadBytesPerSecond,true);
                        DrawText(g,detail,secondary,network,20,82,168);
                    }else {
                        if(!quotaPercent1.HasValue && !quotaPercent2.HasValue) {
                            DrawText(g,detail,secondary,line1,20,32,168);
                            DrawText(g,detail,secondary,line2,20,48,168);
                        }else {
                            DrawMetric(g,detail,amount,primary,secondary,line1,value1.Length==0?"--":value1,quotaPercent1,20);
                            DrawMetric(g,detail,amount,primary,secondary,line2,value2.Length==0?"--":value2,quotaPercent2,114);
                        }
                        DrawText(g,detail,secondary,status,20,82,168);
                    }
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
    static void DrawText(Graphics g,Font font,Brush ink,string text,float x,float y,float width) {
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
            format.FormatFlags|=StringFormatFlags.NoWrap;format.Trimming=StringTrimming.EllipsisCharacter;
            g.DrawString(text,font,ink,new RectangleF(x,y,width,22),format);
        }
    }
    void DrawMetric(Graphics g,Font detail,Font amount,Brush primary,Brush secondary,string label,string value,double? percent,int x) {
        DrawText(g,detail,secondary,label,x,32,74);DrawText(g,amount,primary,value,x,46,74);
        var accent=DefaultAppearance?BubbleAppearance.Text(systemDark):PetTheme.ProgressAccent(panelColor,desktopBackground);
        using(var track=new SolidBrush(Color.FromArgb(38,accent)))
        using(var fill=new SolidBrush(Color.FromArgb(217,accent)))
        using(var shape=Rounded(new RectangleF(x,68.5f,74,3),1.5f)) {
            g.FillPath(track,shape);
            if(percent.HasValue && !Double.IsNaN(percent.Value)) {
                float width=(float)(74*Math.Max(0,Math.Min(100,percent.Value))/100);
                if(width>0)using(var portion=Rounded(new RectangleF(x,68.5f,width,3),Math.Min(1.5f,width/2)))g.FillPath(fill,portion);
            }
        }
    }
    static GraphicsPath Rounded(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    void Follow() {
        if(closing || trayMenuOpen || !IsHandleCreated)return;
        if(!petState.Visible || !bubbleRequested || !AnyModeEnabled) {
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
