using System;
using System.Drawing;
using System.Windows.Forms;

// Real screen acquisition is separate from synthetic colour-policy tests.
// Capture only our test window; no accounts, desktop images or saved captures.
class BubbleCaptureTests {
 [STAThread] static void Main() {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  int checks=0;
  using(var scene=new Form()) {
   var area=Screen.PrimaryScreen.WorkingArea;
   scene.FormBorderStyle=FormBorderStyle.None;scene.ShowInTaskbar=false;scene.TopMost=true;
   scene.StartPosition=FormStartPosition.Manual;scene.Location=new Point(area.Left+40,area.Top+40);
   scene.ClientSize=new Size(260,160);scene.Show();Application.DoEvents();
   foreach(Color source in new[]{Color.FromArgb(35,30,48),Color.FromArgb(240,220,100)})foreach(int percent in new[]{10,20})foreach(Color desktop in new[]{Color.White,Color.Black}) {
    var tint=Color.FromArgb((int)Math.Round(255*percent/100.0),source);
    scene.BackColor=PetTheme.Mix(desktop,tint,tint.A/255.0);scene.Refresh();Application.DoEvents();
    var captured=BubbleBackdrop.Capture(new Rectangle(scene.Left+20,scene.Top+20,190,96),tint);
    if(!captured.HasValue || PetTheme.Distance(captured.Value,desktop)>12)throw new Exception("Real screen capture failed: "+percent+" / "+desktop);
    checks++;
    var foreground=PetTheme.Text(tint,captured);
    var expected=desktop==Color.White?Color.Black:Color.White;
    if(foreground.ToArgb()!=expected.ToArgb())throw new Exception("Real capture selected wrong plain text colour");
    checks++;
   }
  }
  Console.WriteLine("PASS "+checks+" real Windows capture and text colour checks");
 }
}
