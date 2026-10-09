using System;
using System.Drawing;
class BubbleBackdropTests {
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Main() {
  foreach(int percent in new[]{10,20,30,90})foreach(Color desktop in new[]{Color.White,Color.Black,Color.FromArgb(120,160,200)}) {
   var tint=Color.FromArgb((int)Math.Round(255*percent/100.0),35,30,48);
   using(var image=new Bitmap(190,96))using(var g=Graphics.FromImage(image)) {
    g.Clear(PetTheme.Mix(desktop,tint,tint.A/255.0));
    // Even white text must not make a dark background appear bright.
    g.FillRectangle(Brushes.White,24,12,140,17);g.FillRectangle(Brushes.White,24,34,140,17);
    g.FillRectangle(Brushes.White,24,54,140,17);g.FillRectangle(Brushes.White,24,76,140,17);
    var detected=BubbleBackdrop.Read(image,tint);
    Check(detected.HasValue && PetTheme.Distance(detected.Value,desktop)<20,"Tint removal and text exclusion: "+percent+" / "+desktop);
    bool black=PetTheme.Text(tint,detected).ToArgb()==Color.Black.ToArgb();
    bool expected=(percent==10 || percent==20) && PetTheme.Text(PetTheme.Mix(desktop,tint,tint.A/255.0)).ToArgb()==Color.Black.ToArgb();
    Check(black==expected,"Plain text colour follows composed background contrast: "+percent+" / "+desktop);
   }
  }
  using(var image=new Bitmap(190,96))using(var g=Graphics.FromImage(image)) {
   var tint=Color.FromArgb(51,35,30,48);
   g.Clear(PetTheme.Mix(Color.Black,tint,.2));
   using(var fill=new SolidBrush(PetTheme.Mix(Color.White,tint,.2)))g.FillRectangle(fill,172,51,3,3);
   Check(PetTheme.Text(tint,BubbleBackdrop.Read(image,tint)).ToArgb()==Color.Black.ToArgb(),"Bright patches select plain black text");
   Check(!BubbleBackdrop.Read(image,Color.FromArgb(255,35,30,48)).HasValue,"Opaque glass cannot expose the desktop colour");
  }
  using(var image=new Bitmap(8,8))Check(!BubbleBackdrop.Read(image,Color.Black).HasValue,"Invalid capture is ignored");
  Console.WriteLine("PASS "+checks+" background sampling and contrast checks");
 }
}
