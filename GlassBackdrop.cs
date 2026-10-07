using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.UI.Composition;

// A separate compositor surface below the existing per-pixel label.
// Background sampling, GPU blur and the antialiased 32px clip share one visual tree.
sealed class GlassBackdrop : Form {
    ShadowBackdrop shadow;
    readonly string home;
    // One dispatcher queue per UI thread, shared across concurrently visible
    // features and repeated enable/disable cycles. It outlives individual HWNDs.
    [ThreadStatic] static object queue;
    ICompositionTarget compositionTarget;
    CompositionColorBrush tintBrush;
    Compositor compositor;
    CompositionEffectBrush blurBrush;
    CompositionBackdropBrush backdropSource;
    Color tint=Color.FromArgb(178,239,200,78);
    readonly System.Collections.Generic.List<IDisposable> resources=new System.Collections.Generic.List<IDisposable>();
    public bool Ready {get;private set;}
    public GlassBackdrop(string dataDirectory) {
        home=dataDirectory;
        Text="Pet quota glass";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        ClientSize=new Size(190,96);StartPosition=FormStartPosition.Manual;
    }
    T Keep<T>(T value) {var disposable=value as IDisposable;if(disposable!=null)resources.Add(disposable);return value;}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
        get{var p=base.CreateParams;p.ExStyle|=0x00200000|0x00080000|0x08000000|0x80|0x20;return p;}
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}
        base.WndProc(ref m);
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try {
            shadow=new ShadowBackdrop(home);var shadowHandle=shadow.Handle;Owner=shadow;
            if(!SetLayeredWindowAttributes(Handle,0,255,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if(queue==null) {
                var options=new QueueOptions {Size=12,Thread=2,Apartment=2};
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options,out queue));
            }
            compositor=Keep(new Compositor());
            IntPtr raw;((ICompositorDesktopInterop)(object)compositor).CreateDesktopWindowTarget(Handle,true,out raw);
            try {compositionTarget=(ICompositionTarget)Marshal.GetObjectForIUnknown(raw);}
            finally {Marshal.Release(raw);}
            // Host-backdrop mode permits CreateBackdropBrush to sample the desktop.
            // Applying a material to the foreground UpdateLayeredWindow surface caused the old white rectangle.
            SetBackdropMode(5);
            var root=Keep(compositor.CreateContainerVisual());root.Size=new Vector2(Width,Height);
            var geometry=Keep(compositor.CreateRoundedRectangleGeometry());geometry.Size=root.Size;geometry.CornerRadius=new Vector2(32,32);
            root.Clip=Keep(compositor.CreateGeometricClip(geometry));
            var factory=Keep(compositor.CreateEffectFactory(new BackdropGaussian {Name="GlassBlur"}));
            blurBrush=Keep(factory.CreateBrush());
            backdropSource=compositor.CreateBackdropBrush();blurBrush.SetSourceParameter("Backdrop",backdropSource);
            var backdrop=Keep(compositor.CreateSpriteVisual());backdrop.Size=root.Size;backdrop.Brush=blurBrush;
            root.Children.InsertAtBottom(backdrop);
            tintBrush=Keep(compositor.CreateColorBrush(Windows.UI.Color.FromArgb(tint.A,tint.R,tint.G,tint.B)));
            var colourLayer=Keep(compositor.CreateSpriteVisual());colourLayer.Size=root.Size;colourLayer.Brush=tintBrush;
            root.Children.InsertAtTop(colourLayer);compositionTarget.Root=root;
            Ready=true;
            Program.Record("backdrop-blur",new {enabled=true,mode="Composition Gaussian blur",sigma=10,cornerRadius=32});
        }catch(Exception ex) {
            Ready=false;Program.Record("backdrop-blur",new {enabled=false,reason=ex.GetType().Name,hresult=ex.HResult});
        }
    }
    void SetBackdropMode(int state) {
        var policy=new AccentPolicy {State=state};var memory=Marshal.AllocHGlobal(Marshal.SizeOf(policy));
        try {
            Marshal.StructureToPtr(policy,memory,false);
            var data=new CompositionData {Attribute=19,Data=memory,Size=Marshal.SizeOf(policy)};
            if(!SetWindowCompositionAttribute(Handle,ref data))throw new InvalidOperationException("Host backdrop was rejected");
        }finally {Marshal.FreeHGlobal(memory);}
    }
    protected override void OnVisibleChanged(EventArgs e) {
        base.OnVisibleChanged(e);
        if(!Visible || !Ready)return;
        try {
            // A hidden host can leave the backdrop brush sampling its old surface.
            // Re-enable hosting and bind a fresh source whenever it is shown again.
            SetBackdropMode(0);SetBackdropMode(5);
            var next=compositor.CreateBackdropBrush();blurBrush.SetSourceParameter("Backdrop",next);
            var previous=backdropSource;backdropSource=next;if(previous!=null)previous.Dispose();
        }catch(Exception ex) {Program.Record("backdrop-resume-error",new {error=ex.GetType().Name});}
    }
    public void Follow(Point position,bool visible) {
        if(!visible || !Ready){if(Visible)Hide();if(shadow!=null)shadow.Follow(position,false);return;}
        if(shadow!=null)shadow.Follow(position,true);
        if(Location!=position || !Visible)
            Native.SetWindowPos(Handle,new IntPtr(-1),position.X,position.Y,0,0,0x0010|0x0001);
        if(!Visible)Show();
    }
    public void SetTint(Color colour) {
        tint=colour;
        if(tintBrush!=null)tintBrush.Color=Windows.UI.Color.FromArgb(colour.A,colour.R,colour.G,colour.B);
    }
    protected override void Dispose(bool disposing) {
        if(disposing) {
            Owner=null;if(shadow!=null){shadow.Close();shadow.Dispose();shadow=null;}
            if(compositionTarget!=null){compositionTarget.Root=null;compositionTarget=null;}
            if(backdropSource!=null){backdropSource.Dispose();backdropSource=null;}
            for(int i=resources.Count-1;i>=0;i--)resources[i].Dispose();resources.Clear();
            tintBrush=null;
            compositor=null;blurBrush=null;
            // The shared dispatcher controller is retained until UI-thread/process shutdown.
        }
        base.Dispose(disposing);
    }
    [StructLayout(LayoutKind.Sequential)]struct QueueOptions {public int Size,Thread,Apartment;}
    [StructLayout(LayoutKind.Sequential)]struct AccentPolicy {public int State,Flags,GradientColor,AnimationId;}
    [StructLayout(LayoutKind.Sequential)]struct CompositionData {public int Attribute;public IntPtr Data;public int Size;}
    [DllImport("coremessaging.dll")]static extern int CreateDispatcherQueueController(QueueOptions options,[MarshalAs(UnmanagedType.IUnknown)]out object queue);
    [DllImport("user32.dll",SetLastError=true)]static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")]static extern bool SetWindowCompositionAttribute(IntPtr h,ref CompositionData data);
}
[ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICompositorDesktopInterop {void CreateDesktopWindowTarget(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)]bool topmost,out IntPtr target);}
[ComImport,Guid("A1BEA8BA-D726-4663-8129-6B5E7927FFA6"),InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
interface ICompositionTarget {Visual Root{get;set;}}
