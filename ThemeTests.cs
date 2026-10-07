using System;
using System.Drawing;
class ThemeTests {
 static void Require(bool b,string message) {if(!b){Console.WriteLine(message);Environment.Exit(1);}}
 static void Main(string[] args) {
  foreach(Color c in new [] {Color.Gold,Color.RoyalBlue,Color.ForestGreen,Color.Red,Color.White,Color.DarkGray,Color.Purple}) {
   Color bg=PetTheme.Background(c);
   Require(bg.R==c.R && bg.G==c.G && bg.B==c.B && bg.A==230,"Only alpha may change: "+c);
   Require(PetTheme.Contrast(bg,PetTheme.Text(bg))>=4.5,"Primary contrast: "+c);
   Require(PetTheme.Contrast(bg,PetTheme.Secondary(bg))>=4.5,"Secondary contrast: "+c);
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
