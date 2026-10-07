using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// CSS-equivalent outer shadow: 0 3px 16px, black alpha .20 (light) / .35 (dark).
// A separate per-pixel surface leaves room outside the 190x96 glass panel.
sealed class ShadowBackdrop : Form {
    const int ShadowPadding=24,PanelWidth=190,PanelHeight=96;
    readonly Bitmap pixels;
    public ShadowBackdrop(string home) {
        Text="Pet quota shadow";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        ClientSize=new Size(PanelWidth+ShadowPadding*2,PanelHeight+ShadowPadding*2);StartPosition=FormStartPosition.Manual;
        int alpha=IsDark(home)?89:51;
        pixels=Build(alpha);
        Program.Record("panel-shadow",new {offsetX=0,offsetY=3,blur=16,alpha=alpha,padding=ShadowPadding});
    }
    static bool IsDark(string home) {
        string theme="system";
        try {
            foreach(var line in File.ReadLines(Path.Combine(home,"config.toml"))) {
                if(!line.TrimStart().StartsWith("appearanceTheme"))continue;
                var parts=line.Split('=');if(parts.Length==2)theme=parts[1].Trim().Trim('"');
            }
        }catch(IOException) { }
        if(theme=="dark")return true;if(theme=="light")return false;
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            return key!=null && Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;
    }
    // Analytic antialiasing of a rounded rectangle; shift applies only to the shadow source.
    static double Coverage(double x,double y,double offsetY) {
        double dx=Math.Abs(x-PanelWidth/2.0)-(PanelWidth/2.0-32);
        double dy=Math.Abs(y-offsetY-PanelHeight/2.0)-(PanelHeight/2.0-32);
        double d=Math.Sqrt(Math.Max(0,dx)*Math.Max(0,dx)+Math.Max(0,dy)*Math.Max(0,dy))+Math.Min(Math.Max(dx,dy),0)-32;
        return Math.Max(0,Math.Min(1,.5-d));
    }
    static Bitmap Build(int opacity) {
        int w=PanelWidth+2*ShadowPadding,h=PanelHeight+2*ShadowPadding;
        var mask=new double[w*h];var horizontal=new double[w*h];var output=new byte[w*h*4];
        var kernel=new double[49];double total=0;
        for(int i=-24;i<=24;i++){kernel[i+24]=Math.Exp(-i*i/(2.0*8*8));total+=kernel[i+24];}
        for(int i=0;i<kernel.Length;i++)kernel[i]/=total;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)mask[y*w+x]=Coverage(x+.5-ShadowPadding,y+.5-ShadowPadding,3);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
            double v=0;for(int k=-24;k<=24;k++)if(x+k>=0 && x+k<w)v+=mask[y*w+x+k]*kernel[k+24];horizontal[y*w+x]=v;
        }
        for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
            double v=0;for(int k=-24;k<=24;k++)if(y+k>=0 && y+k<h)v+=horizontal[(y+k)*w+x]*kernel[k+24];
            // Outer box-shadow stays outside the panel, even when its tint is translucent.
            v*=1-Coverage(x+.5-ShadowPadding,y+.5-ShadowPadding,0);
            output[(y*w+x)*4+3]=(byte)Math.Round(v*opacity);
        }
        var bitmap=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
        var data=bitmap.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try{Marshal.Copy(output,0,data.Scan0,output.Length);}finally{bitmap.UnlockBits(data);}
        return bitmap;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x20|0x80000;return p;}}
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        var screen=Native.GetDC(IntPtr.Zero);var dc=Native.CreateCompatibleDC(screen);
        var bitmap=pixels.GetHbitmap(Color.FromArgb(0));var previous=Native.SelectObject(dc,bitmap);
        try {
            var position=new Native.POINT {X=Left,Y=Top};var origin=new Native.POINT();
            var size=new Native.SIZE {Width=Width,Height=Height};var blend=new Native.BLEND {SourceConstantAlpha=255,AlphaFormat=1};
            if(!Native.UpdateLayeredWindow(Handle,screen,ref position,ref size,dc,ref origin,0,ref blend,2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }finally {Native.SelectObject(dc,previous);Native.DeleteObject(bitmap);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
    }
    public void Follow(Point panel,bool visible) {
        if(!visible){if(Visible)Hide();return;}
        var position=new Point(panel.X-ShadowPadding,panel.Y-ShadowPadding);
        if(Location!=position || !Visible)Native.SetWindowPos(Handle,new IntPtr(-1),position.X,position.Y,0,0,0x0010|0x0001);
        if(!Visible)Show();
    }
    protected override void Dispose(bool disposing){if(disposing)pixels.Dispose();base.Dispose(disposing);}
}
