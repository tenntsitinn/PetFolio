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
sealed class PetState {
    public static readonly PetState Empty = new PetState(IntPtr.Zero,Point.Empty,112,false,false,false,"",null);
    public IntPtr Target { get; private set; }
    public Point Anchor { get; private set; }
    public int Size { get; private set; }
    public bool AnchorKnown { get; private set; }
    public bool Visible { get; private set; }
    public bool Dragging { get; private set; }
    public string PetId { get; private set; }
    public Color? Colour { get; private set; }
    public Rectangle Bounds { get { return new Rectangle(Anchor.X,Anchor.Y,Size,(int)Math.Ceiling(Size*208.0/192)); } }
    public PetState(IntPtr target,Point anchor,int size,bool known,bool visible,bool dragging,string petId,Color? colour) {
        Target=target;Anchor=anchor;Size=size;AnchorKnown=known;Visible=visible;Dragging=dragging;PetId=petId;Colour=colour;
    }
}

interface IPetStateSource {
    PetState Current { get; }
    event Action<PetState> Changed;
}

sealed class PetStateService : IPetStateSource, IDisposable {
    readonly string home;
    readonly Action<Action> dispatch;
    readonly System.Windows.Forms.Timer follow = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer colourSample = new System.Windows.Forms.Timer();
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly PetDragFollower drag = new PetDragFollower();
    readonly PetColourSwitch petSwitch = new PetColourSwitch();
    readonly PetPaletteCatalog palettes;
    PetMouseObserver mouse;
    IntPtr target;
    DateTime fileTime;
    Point anchor,savedAnchor;
    long nextMetadata,nextDiscovery;
    int petSize=112;
    bool anchorKnown,petOpen,colourBusy,dragStarted,dragReleased,started;
    volatile bool closing;
    Color? colour;
    public PetState Current { get; private set; }
    public event Action<PetState> Changed;
    public PetStateService(string home,string palettePath,Action<Action> dispatch) {
        this.home=home;this.dispatch=dispatch;palettes=new PetPaletteCatalog(palettePath);
        Current=PetState.Empty;
        follow.Interval=16;follow.Tick+=(s,e)=>Tick();
        colourSample.Interval=150;colourSample.Tick+=(s,e)=>SamplePetColour();
    }
    public void Start() {
        if(closing)throw new ObjectDisposedException("PetStateService");
        if(started)return;started=true;
        try {mouse=new PetMouseObserver(MouseChanged);}
        catch(Exception ex){Program.Record("drag-observer",new {enabled=false,error=ex.GetType().Name});}
        follow.Start();colourSample.Start();Tick();SamplePetColour();
    }
    void Publish(bool visible) {
        Current=new PetState(target,anchor,petSize,anchorKnown,visible,drag.Active,petSwitch.Pet,colour);
        var handler=Changed;if(handler!=null)handler(Current);
    }
    public void Dispose() {
        if(closing)return;closing=true;
        follow.Stop();colourSample.Stop();follow.Dispose();colourSample.Dispose();
        if(mouse!=null){mouse.Dispose();mouse=null;}
        drag.Reset();Changed=null;
    }
    void SamplePetColour() {
        if(!petSwitch.CanSample(watch.ElapsedMilliseconds) || colourBusy || closing || !Current.Visible || target==IntPtr.Zero || !anchorKnown)return;
        colourBusy=true;
        var handle=target;var expectedAnchor=anchor;var generation=petSwitch.Generation;int size=petSize;
        Task.Factory.StartNew(()=> {
            Color? sample=null;
            try {
                Native.RECT r;
                if(Native.GetWindowRect(handle,out r)) {
                    int w=r.Right-r.Left,h=r.Bottom-r.Top;
                    if(w>0 && h>0 && w<=8192 && h<=8192) {
                        var area=new Rectangle(expectedAnchor.X-r.Left,expectedAnchor.Y-r.Top,size,(int)Math.Ceiling(size*208.0/192));
                        var clipped=Rectangle.Intersect(area,new Rectangle(0,0,w,h));
                        if(clipped.Width*clipped.Height>=area.Width*area.Height*.8) {
                            using(var image=new Bitmap(w,h)) {
                                bool ok;
                                using(var g=Graphics.FromImage(image)) {var dc=g.GetHdc();try{ok=Native.PrintWindow(handle,dc,2);}finally{g.ReleaseHdc(dc);} }
                                if(ok)sample=PetTheme.Dominant(image,clipped);
                            }
                        }
                    }
                }
            }catch { }
            if(closing)return;
            try {dispatch(()=> {
                if(closing)return;
                colourBusy=false;
                ReadSelection();
                if(generation!=petSwitch.Generation) {
                    Program.Record("pet-colour-discarded",new {generation=generation,currentGeneration=petSwitch.Generation,reason="selection changed"});return;
                }
                if(target!=handle || anchor!=expectedAnchor)sample=null;
                if(!petSwitch.AcceptSample(generation,sample,watch.ElapsedMilliseconds))return;
                ApplyPetColour(sample.Value,"pet-window-render-confirmed");
            });}catch(InvalidOperationException){ }
        });
    }
    void ReadSelection() {
        var pet=PetSelectionConfig.Read(Path.Combine(home,"config.toml"));
        if(petSwitch.Select(pet,watch.ElapsedMilliseconds)) {
            
            Program.Record("pet-selection",new {pet=petSwitch.Pet,generation=petSwitch.Generation});
        }
        if(petSwitch.HasColour)return;
        Color colour;
        if(palettes.TryGet(petSwitch.Pet,out colour) && petSwitch.ApplySource(petSwitch.Generation,colour))
            ApplyPetColour(colour,"sprite-source-catalog");
    }
    void ApplyPetColour(Color value,string source) {
        colour=value;
        Program.Record("pet-theme",new {source=source,r=value.R,g=value.G,b=value.B,pet=petSwitch.Pet,generation=petSwitch.Generation});
    }
    void ReadAnchor() {
        var path=Path.Combine(home,".codex-global-state.json");
        try {
            var modified=File.GetLastWriteTimeUtc(path);
            if(modified==fileTime && anchorKnown)return;
            Dictionary<string,object> root;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream)) root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
            var b=Find(root,"electron-avatar-overlay-bounds") as Dictionary<string,object>;
            var open=Find(root,"electron-avatar-overlay-open");
            petOpen=open is bool && (bool)open;
            if(b!=null && b.ContainsKey("x") && b.ContainsKey("y")) {
                savedAnchor=new Point(Convert.ToInt32(b["x"]),Convert.ToInt32(b["y"]));anchorKnown=true;
            }
            fileTime=modified;
            // Read only the pet size field, never copy unrelated user configuration.
            foreach(var line in File.ReadLines(Path.Combine(home,"config.toml")))
                if(line.Contains("avatar-overlay-mascot-width-px")) {
                    int value; var pieces=line.Split('=');
                    if(pieces.Length==2 && int.TryParse(pieces[1].Trim(),out value))petSize=Math.Max(80,Math.Min(224,value));
                }
        } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
    }
    static object Find(Dictionary<string,object> d,string key) {
        object v;if(d.TryGetValue(key,out v))return v;
        foreach(var x in d.Values) { var child=x as Dictionary<string,object>;if(child!=null){v=Find(child,key);if(v!=null)return v;} }
        return null;
    }
    void Tick() {
        if(closing)return;
        if(watch.ElapsedMilliseconds>=nextMetadata) {
            nextMetadata=watch.ElapsedMilliseconds+100;
            ReadSelection();ReadAnchor();
        }
        // Missing pets still use the discovery interval; do not enumerate all
        // desktop windows at 16 ms just because the last target was zero.
        if(watch.ElapsedMilliseconds>=nextDiscovery || (target!=IntPtr.Zero && !Native.IsWindow(target))) {
            nextDiscovery=watch.ElapsedMilliseconds+2000;
            var candidates=new List<IntPtr>();
            Native.EnumWindows((hwnd,param) => {
                var cls=new StringBuilder(128);Native.GetClassName(hwnd,cls,cls.Capacity);
                long ex=Native.GetWindowLongPtr(hwnd,-20).ToInt64();
                if(cls.ToString()=="Chrome_WidgetWin_1" && (ex & 0x80008)==0x80008) {
                    uint pid;Native.GetWindowThreadProcessId(hwnd,out pid);
                    try {
                        using(var p=Process.GetProcessById((int)pid))
                            if(p.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)
                                && p.MainModule.FileName.IndexOf("OpenAI.Codex",StringComparison.OrdinalIgnoreCase)>=0)candidates.Add(hwnd);
                    }catch { }
                }
                return true;
            },IntPtr.Zero);
            IntPtr found=candidates.Count==1 ? candidates[0] : IntPtr.Zero;
            if(found!=target) { drag.Reset();target=found;Program.Record("target",new { hwnd=target.ToInt64(), candidates=candidates.Count }); }
        }
        bool visible=anchorKnown && petOpen && target!=IntPtr.Zero && Native.IsWindowVisible(target) && !Native.IsIconic(target);
        if(!visible){drag.Reset();anchor=savedAnchor;Publish(false);return;}
        if(drag.Active) {
            Native.POINT cursor;
            if(Native.GetCursorPos(out cursor))MoveDrag(new Point(cursor.X,cursor.Y));
            // Also recover if Windows omitted a hook release event.
            int button=Native.GetSystemMetrics(23)!=0?2:1;
            if((Native.GetAsyncKeyState(button)&0x8000)==0){drag.End(watch.ElapsedMilliseconds);dragReleased=true;}
        }
        anchor=drag.Resolve(savedAnchor,watch.ElapsedMilliseconds);
        if(dragStarted){dragStarted=false;Program.Record("drag-start",new { anchorX=anchor.X,anchorY=anchor.Y });}
        if(dragReleased){dragReleased=false;Program.Record("drag-release",new { anchorX=anchor.X,anchorY=anchor.Y });}
        Publish(true);
    }
    void MouseChanged(Point pointer,bool down) {
        if(closing)return;
        if(down) {
            if((Native.GetAsyncKeyState(17)&0x8000)!=0)return;
            if(!anchorKnown || !petOpen || target==IntPtr.Zero || !Native.IsWindowVisible(target))return;
            var pet=new Rectangle(anchor.X,anchor.Y,petSize,(int)Math.Ceiling(petSize*208.0/192));
            if(!pet.Contains(pointer))return;
            var p=new Native.POINT { X=pointer.X,Y=pointer.Y };
            if(Native.GetAncestor(Native.WindowFromPoint(p),2)!=target)return;
            drag.Begin(pointer,anchor,savedAnchor);dragStarted=true;
        }else if(drag.Active) {
            MoveDrag(pointer);drag.End(watch.ElapsedMilliseconds);dragReleased=true;
        }
    }
    void MoveDrag(Point pointer) {
        var screen=Screen.FromPoint(pointer);
        // Native pet layout uses the work area's top/left and the display's bottom,
        // reserving 8px below the mascot plus the 32px pet control row.
        var area=Rectangle.FromLTRB(screen.WorkingArea.Left,screen.WorkingArea.Top,
            screen.WorkingArea.Right,screen.Bounds.Bottom);
        drag.Move(pointer,area,petSize,40);
    }
}
