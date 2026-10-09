using System;
using System.Drawing;
using System.Runtime.InteropServices;

// One small, in-memory capture per accepted quota refresh. Sample blank margins
// rather than glyphs, and undo the tint to estimate the blurred desktop beneath.
static class BubbleBackdrop {
    internal static Color? Capture(Rectangle bounds,Color tint) {
        try {
            DwmFlush();
            using(var image=new Bitmap(bounds.Width,bounds.Height)) {
                // Framework Graphics.CopyFromScreen rejects combined enum flags.
                // Native BitBlt accepts SRCCOPY | CAPTUREBLT and includes glass.
                using(var g=Graphics.FromImage(image)) {
                    var screen=GetDC(IntPtr.Zero);
                    if(screen==IntPtr.Zero)return null;
                    try {
                        var destination=g.GetHdc();
                        try {
                            if(!BitBlt(destination,0,0,bounds.Width,bounds.Height,screen,bounds.X,bounds.Y,0x40CC0020))return null;
                        }finally{g.ReleaseHdc(destination);}
                    }finally{ReleaseDC(IntPtr.Zero,screen);}
                }
                return Read(image,tint);
            }
        }catch(System.ComponentModel.Win32Exception){return null;}
        catch(ExternalException){return null;}
        catch(ArgumentException){return null;}
    }
    internal static Color? Read(Bitmap image,Color tint) {
        // Fully opaque glass provides no information about the desktop below it.
        if(image.Width<190 || image.Height<96 || tint.A==255)return null;
        Color brightest=Color.Black;double maximum=-1;
        foreach(int x in new[]{16,173})foreach(int y in new[]{32,52,72}) {
            int r=0,g=0,b=0;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
                var c=image.GetPixel(x+dx,y+dy);r+=c.R;g+=c.G;b+=c.B;
            }
            var surface=Color.FromArgb(r/9,g/9,b/9);
            var desktop=Color.FromArgb(Untint(surface.R,tint.R,tint.A),Untint(surface.G,tint.G,tint.A),Untint(surface.B,tint.B,tint.A));
            double luminance=PetTheme.Luminance(desktop);
            // Bright areas are the worst case for light ink; preserve protection
            // when the desktop contains both light and dark regions.
            if(luminance>maximum){maximum=luminance;brightest=desktop;}
        }
        return brightest;
    }
    static int Untint(byte observed,byte tint,byte alpha) {
        return Math.Max(0,Math.Min(255,(int)Math.Round((observed*255.0-tint*alpha)/(255-alpha))));
    }
    [DllImport("dwmapi.dll")]static extern int DwmFlush();
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll",SetLastError=true)]static extern bool BitBlt(IntPtr destination,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
}
