using System;
using System.Drawing;
class ThemeTests {
 static void Require(bool b,string message) {if(!b){Console.WriteLine(message);Environment.Exit(1);}}
 static void Main(string[] args) {
  foreach(Color c in new [] {Color.Gold,Color.RoyalBlue,Color.ForestGreen,Color.Red,Color.White,Color.DarkGray,Color.Purple}) {
   Color bg=PetTheme.Background(c);
   Require(bg.R==c.R && bg.G==c.G && bg.B==c.B && bg.A==230,"Only alpha may change: "+c);
   Require(PetTheme.Contrast(bg,PetTheme.Text(bg))>=4.5,"Primary contrast: "+c);
   Require(PetTheme.Text(bg).A==255 && PetTheme.Secondary(bg).A==230,"Primary alpha 1 and secondary alpha approximately .9: "+c);
   Require(PetTheme.RenderedContrast(bg,PetTheme.Secondary(bg))<PetTheme.Contrast(bg,PetTheme.Text(bg)),"Secondary ink is softer after compositing: "+c);
  }
  Color translucentDark=Color.FromArgb(51,35,30,48);
  for(int percent=10;percent<=100;percent+=10) {
   int alpha=(int)Math.Round(255*percent/100.0);
   Require((PetTheme.Text(Color.FromArgb(alpha,35,30,48),Color.White).ToArgb()==Color.Black.ToArgb())==(percent==10 || percent==20),"Dark tint switches to black only at 10% and 20%: "+percent);
   Require(PetTheme.Text(Color.FromArgb(alpha,35,30,48),Color.Black).ToArgb()==Color.White.ToArgb(),"Dark desktop retains white text: "+percent);
   Require(PetTheme.Text(Color.FromArgb(alpha,240,220,100),Color.White).ToArgb()==PetTheme.Text(Color.FromArgb(alpha,240,220,100)).ToArgb(),"Light tint keeps existing text: "+percent);
   Require((PetTheme.Text(Color.FromArgb(alpha,240,220,100),Color.Black).ToArgb()==Color.White.ToArgb())==(percent==10 || percent==20),"Light tint switches to white on dark desktop only at 10% and 20%: "+percent);
   Require(PetTheme.Secondary(Color.FromArgb(alpha,240,220,100),Color.Black).A==230,"Light tint secondary ink retains .9 alpha: "+percent);
  }
  Require(PetTheme.Text(translucentDark,null).ToArgb()==Color.White.ToArgb(),"Unknown desktop retains the original colour");
  Require(PetTheme.Text(Color.FromArgb(51,240,220,100),null).ToArgb()==Color.Black.ToArgb(),"Unknown desktop retains original light-tint text");
  Require(PetTheme.Secondary(Color.FromArgb(51,240,220,100),Color.Black).ToArgb()==Color.FromArgb(230,Color.White).ToArgb(),"Updated time uses white at .9 alpha for light tint on dark desktop");
  Require(PetTheme.Secondary(translucentDark,Color.White).ToArgb()==Color.FromArgb(230,Color.Black).ToArgb(),"Updated time uses black at .9 alpha on light desktop");
  foreach(Color desktop in new[]{Color.White,Color.Black,Color.FromArgb(150,150,150)}) {
   var surface=PetTheme.Mix(desktop,translucentDark,translucentDark.A/255.0);
   var primary=PetTheme.Text(translucentDark,desktop);var secondary=PetTheme.Secondary(translucentDark,desktop);
   Require(primary.A==255 && secondary.A==230 && primary.R==secondary.R && primary.G==secondary.G && primary.B==secondary.B,"Depth is distinguished by alpha rather than different RGB");
   Require(Math.Abs(PetTheme.RenderedContrast(surface,secondary)-PetTheme.Contrast(surface,PetTheme.Mix(surface,secondary,230/255.0)))<.000001,"Secondary contrast includes alpha blending");
   var alternative=Color.FromArgb(230,primary.R==0?Color.White:Color.Black);
   Require(PetTheme.RenderedContrast(surface,secondary)>=PetTheme.RenderedContrast(surface,alternative),"Select the higher contrast ink after alpha blending");
  }
  foreach(Color desktop in new [] {Color.White,Color.Black})using(var b=new Bitmap(180,40)) {
   using(var g=Graphics.FromImage(b))using(var font=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel))using(var ink=new SolidBrush(PetTheme.Text(translucentDark,desktop))) {
    g.Clear(PetTheme.Mix(desktop,translucentDark,translucentDark.A/255.0));
    g.DrawString("Week 51%",font,ink,new RectangleF(8,8,160,24),StringFormat.GenericTypographic);
   }
   int dark=0,light=0;
   for(int y=8;y<28;y++)for(int x=8;x<90;x++) {
    var pixel=b.GetPixel(x,y);if(PetTheme.Luminance(pixel)<.1)dark++;if(PetTheme.Luminance(pixel)>.8)light++;
   }
   Require(desktop==Color.White?dark>20 && light==0:light>20,"Plain black or white glyphs without outline on "+desktop);
  }
  using(var b=new Bitmap(112,122)) {
   using(var g=Graphics.FromImage(b)) {
    g.Clear(Color.Black);g.FillRectangle(Brushes.Gold,30,20,50,80);
    g.FillRectangle(Brushes.White,40,45,30,30);g.FillRectangle(Brushes.Green,45,25,8,8);
   }
   Color? found=PetTheme.Dominant(b,new Rectangle(0,0,112,122));
   Require(found.HasValue && PetTheme.Distance(found.Value,Color.Gold)<20,"Body must dominate canvas, belly and eyes");
   using(var g=Graphics.FromImage(b))g.Clear(Color.Black);
   Require(!PetTheme.Dominant(b,new Rectangle(0,0,112,122)).HasValue,"Empty capture must not change the theme");
   foreach(Color colour in new [] {Color.RoyalBlue,Color.Red,Color.ForestGreen}) {
    using(var g=Graphics.FromImage(b)){g.Clear(Color.Black);using(var body=new SolidBrush(colour))g.FillRectangle(body,30,20,50,80);}
    var changed=PetTheme.Dominant(b,new Rectangle(0,0,112,122));
    Require(changed.HasValue && PetTheme.Distance(changed.Value,colour)<20,"Frame colour change must be detected");
   }
  }
  foreach(var file in args)using(var b=new Bitmap(file)) {
   var found=PetTheme.Dominant(b,new Rectangle(0,0,b.Width,b.Height));
   Require(found.HasValue,"Real pet crop must yield a colour");
   Console.WriteLine(System.IO.Path.GetFileName(file)+": "+ColorTranslator.ToHtml(found.Value));
  }
  Console.WriteLine("Theme checks passed");
 }
}
