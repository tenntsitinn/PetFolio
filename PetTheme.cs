using System;
using System.Drawing;

// Independently implemented. Inspired by ATBC's source-colour/contrast-correction separation.
// https://github.com/atbc-org/Adaptive-Tab-Bar-Colour/blob/main/src/utils/colour.ts
static class PetTheme {
    public static Color? Dominant(Bitmap image, Rectangle area) {
        area=Rectangle.Intersect(area,new Rectangle(0,0,image.Width,image.Height));
        if(area.Width<8 || area.Height<8)return null;
        double[] weights=new double[24],rs=new double[24],gs=new double[24],bs=new double[24];
        double neutralR=0,neutralG=0,neutralB=0;int neutralCount=0,visibleCount=0;
        for(int y=area.Top;y<area.Bottom;y+=2)for(int x=area.Left;x<area.Right;x+=2) {
            Color c=image.GetPixel(x,y);
            int max=Math.Max(c.R,Math.Max(c.G,c.B)), min=Math.Min(c.R,Math.Min(c.G,c.B));
            // PrintWindow renders transparent canvas as black; skip canvas and dark outlines.
            if(c.A<32 || max<35)continue;
            visibleCount++;
            double saturation=max==0?0:(max-min)/(double)max;
            if(saturation<0.18) { neutralR+=c.R;neutralG+=c.G;neutralB+=c.B;neutralCount++;continue; }
            int bin=((int)((c.GetHue()+7.5)/15))%24;
            double weight=0.35+0.65*saturation;
            weights[bin]+=weight;rs[bin]+=weight*c.R;gs[bin]+=weight*c.G;bs[bin]+=weight*c.B;
        }
        if(visibleCount<20)return null;
        int best=0;for(int i=1;i<24;i++)if(weights[i]>weights[best])best=i;
        if(weights[best]>=Math.Max(12,visibleCount*0.06))
            return Color.FromArgb((int)(rs[best]/weights[best]),(int)(gs[best]/weights[best]),(int)(bs[best]/weights[best]));
        if(neutralCount>=20)return Color.FromArgb((int)(neutralR/neutralCount),(int)(neutralG/neutralCount),(int)(neutralB/neutralCount));
        return null;
    }
    public static double Luminance(Color c) { return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B); }
    static double Channel(byte n) { double s=n/255.0;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4); }
    public static double Contrast(Color a,Color b) {
        double x=Luminance(a),y=Luminance(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    public static Color Mix(Color a,Color b,double fraction) {
        fraction=Math.Max(0,Math.Min(1,fraction));
        return Color.FromArgb((int)Math.Round(a.R+(b.R-a.R)*fraction),
            (int)Math.Round(a.G+(b.G-a.G)*fraction),(int)Math.Round(a.B+(b.B-a.B)*fraction));
    }
    public static Color Background(Color source) {
        return Color.FromArgb(230,source.R,source.G,source.B);
    }
    public static Color Text(Color background) {
        Color dark=Color.Black,light=Color.White;
        return Contrast(background,dark)>=Contrast(background,light)?dark:light;
    }
    // A subtle hairline uses the text RGB, independently of the glass opacity.
    public static Color Outline(Color background) {return Color.FromArgb(60,Text(background));}
    public static Color Secondary(Color background) {
        return Color.FromArgb(230,Text(background));
    }
    public static double RenderedContrast(Color surface,Color ink) {
        return Contrast(surface,Mix(surface,ink,ink.A/255.0));
    }
    // At 10% / 20%, the desktop can reverse the tint's apparent brightness.
    // Choose plain ink from the cached composite for both light and dark pets.
    public static Color Text(Color background,Color? desktop) {
        var original=Text(background);
        if((background.A!=26 && background.A!=51) || !desktop.HasValue)return original;
        var surface=Mix(desktop.Value,background,background.A/255.0);
        // Select the shared RGB using the softer status text; the primary ink
        // is opaque and therefore has at least the same contrast.
        return RenderedContrast(surface,Color.FromArgb(230,Color.Black))>=RenderedContrast(surface,Color.FromArgb(230,Color.White))?Color.Black:Color.White;
    }
    public static Color Secondary(Color background,Color? desktop) {
        return Color.FromArgb(230,Text(background,desktop));
    }
    public static int Distance(Color a,Color b) {return Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}
}
