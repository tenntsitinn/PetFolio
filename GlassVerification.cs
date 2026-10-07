using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
// Controlled visual check of the production blur and label; no quota requests or settings writes.
class GlassVerification : Form {
 public bool Alternate;
 bool Green;
 QuotaLabel label;
 readonly EventWaitHandle exitEvent=new EventWaitHandle(false,EventResetMode.ManualReset);
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=500};
 int stage;Bitmap first;Point origin=new Point(700,350);int failures;
 protected override void OnPaint(PaintEventArgs e) {
  if(Green) {
   e.Graphics.Clear(Color.FromArgb(202,230,205));
   using(var pen=new Pen(Color.FromArgb(158,196,167)))for(int y=0;y<Height;y+=18)e.Graphics.DrawLine(pen,0,y,Width,y);
   return;
  }
  e.Graphics.Clear(Color.White);
  for(int x=0;x<Width;x+=8)e.Graphics.FillRectangle(Brushes.Black,x,0,4,Height);
  e.Graphics.FillRectangle(Alternate?Brushes.Red:Brushes.RoyalBlue,95,0,95,Height);
 }
 GlassVerification() {
  Text="Glass verification pattern";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
  StartPosition=FormStartPosition.Manual;Location=new Point(675,325);ClientSize=new Size(260,150);
  Shown+=(s,e)=>{
   label=new QuotaLabel("",true);label.Show();
   label.PreviewGlass(Color.FromArgb(102,239,200,78),origin);timer.Start();
  };
  timer.Tick+=(s,e)=>{
   try {
    if(stage==0) {
     first=CaptureFrame("glass-40-percent.png");
     int min=255,max=0;
     for(int x=10;x<21;x++){int v=first.GetPixel(x+5,45).R;min=Math.Min(min,v);max=Math.Max(max,v);}
     Check(max-min<30,"Thin background stripes blurred (R range="+(max-min)+")");
     var point=new Native.POINT {X=origin.X+100,Y=origin.Y+40};
     var hit=WindowFromPoint(point);
     Check(hit==label.Handle,"Rounded panel body receives input for dragging");
     var cornerHit=WindowFromPoint(new Native.POINT {X=origin.X,Y=origin.Y});
     Check(cornerHit!=label.Handle && cornerHit!=label.Owner.Handle && cornerHit!=label.Owner.Owner.Handle,"Transparent corner allows mouse input through");
     var shadowHit=WindowFromPoint(new Native.POINT {X=origin.X+100,Y=origin.Y+label.Height+7});
     Check(shadowHit!=label.Handle && shadowHit!=label.Owner.Handle && shadowHit!=label.Owner.Owner.Handle,"Shadow padding allows mouse input through");
     label.PreviewHover(new Point(origin.X-30,origin.Y-30));
     Check(!label.DismissButton.Visible,"Close button hidden when pointer is outside bubble");
     label.PreviewHover(new Point(origin.X+80,origin.Y+40));
     Check(label.DismissButton.Visible,"Hovering bubble reveals close button");
     var closePosition=new Point(origin.X+CloseBubbleButton.OffsetX,origin.Y+CloseBubbleButton.OffsetY);
     Check(label.DismissButton.Location==closePosition,"Close button attaches to the bubble upper left");
     var closeCenter=new Point(closePosition.X+10,closePosition.Y+10);
     label.PreviewHover(closeCenter);
     Check(label.DismissButton.Visible,"Moving from bubble onto close button keeps it visible");
     label.PreviewHover(new Point(origin.X-30,origin.Y-30));
     Check(!label.DismissButton.Visible,"Leaving both bubble and close button hides the button");
     label.PreviewHover(closeCenter);
     Check(!label.DismissButton.Visible,"Invisible close area does not reveal a button by itself");
     label.PreviewHover(new Point(origin.X+80,origin.Y+40));
     SendMessage(label.DismissButton.Handle,0x201,new IntPtr(1),new IntPtr(10|(10<<16)));
     SendMessage(label.DismissButton.Handle,0x202,IntPtr.Zero,new IntPtr(10|(10<<16)));
     Check(!label.Visible && !label.DismissButton.Visible && !label.Owner.Visible && !label.Owner.Owner.Visible,"Clicking close hides every bubble surface");
     Check(!label.BubbleRequested && !label.IsDisposed && Native.IsWindow(label.Handle),"Close dismisses bubble without ending its form or process");
     Check(label.ShowBubbleCommand.Enabled,"Tray restore command enabled after dismissal");
     label.ShowBubbleCommand.PerformClick();
     Check(label.Visible && label.Owner.Visible && label.BubbleRequested && !label.ShowBubbleCommand.Enabled,"Tray command restores the same live bubble");
     Check(!label.DismissButton.Visible,"Restoring bubble keeps close button hidden until hover");
     Color outside=first.GetPixel(5,5);Check(outside.R==outside.G && outside.G==outside.B,"Top-left corner remains un-tinted background");
     var shadowPixel=first.GetPixel(105,label.Height+8);
     Check(shadowPixel.B<225 && shadowPixel.B>150,"Soft shadow extends below the panel without a solid backing");
     Alternate=true;Invalidate();
    }else if(stage==1) {
     using(var changed=CaptureFrame("glass-updated-background.png")) {
      Color a=first.GetPixel(110,45),b=changed.GetPixel(110,45);
      Check(Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)>80,"Backdrop updates when the background changes");
     }
     label.PreviewGlass(Color.FromArgb(255,239,200,78),origin);
    }else if(stage==2) {
     using(var opaque=CaptureFrame("glass-100-percent.png")) {
      var c=opaque.GetPixel(110,38);Check(c.R==239 && c.G==200 && c.B==78,"100% tint preserves exact RGB");
      var black=opaque.GetPixel(35,37); // A dark text pixel is also checked visually in saved frames.
     }
     origin=new Point(725,360);label.PreviewGlass(Color.FromArgb(102,239,200,78),origin);
    }else if(stage==3) {
     using(var moved=CaptureFrame("glass-moved.png"))Check(label.Location==origin,"Text and glass follow the same position");
     label.Hide();if(label.Owner!=null)((GlassBackdrop)label.Owner).Follow(origin,false);
    }else if(stage==4) {
     using(var hidden=CaptureFrame("glass-hidden.png")) {
      var c=hidden.GetPixel(110,45);Check(c.R==255 && c.G==0 && c.B==0,"Hiding both surfaces reveals the original background");
      var edge=hidden.GetPixel(105,label.Height+8);Check(edge.R==255 && edge.G==0 && edge.B==0,"Shadow hides with the panel");
     }
     Green=true;Invalidate();label.Show();label.PreviewGlass(Color.FromArgb(178,239,200,78),origin);
    }else {
     using(var green=CaptureFrame("glass-green-edge.png")) {
      int whitePixels=0;
      for(int y=0;y<green.Height;y++)for(int x=0;x<green.Width;x++) {
       var c=green.GetPixel(x,y);if(c.R>240 && c.G>240 && c.B>240)whitePixels++;
      }
      Check(whitePixels==0,"Green backdrop has no white fringe pixels");
     }
     Console.WriteLine(failures==0?"Glass visual checks passed":"Glass visual checks failed: "+failures);Close();
    }
    stage++;
   }catch(Exception ex){Console.WriteLine(ex);failures++;Close();}
  };
  FormClosed+=(s,e)=>{timer.Stop();if(label!=null){label.Close();label.Dispose();}if(first!=null)first.Dispose();exitEvent.Dispose();Environment.ExitCode=failures==0?0:1;};
 }
 Bitmap CaptureFrame(string name) {
  var b=new Bitmap(label.Width+10,label.Height+10);using(var g=Graphics.FromImage(b))g.CopyFromScreen(origin.X-5,origin.Y-5,0,0,b.Size);
  b.Save(Path.Combine(Program.Folder,name),ImageFormat.Png);return b;
 }
 void Check(bool ok,string message){Console.WriteLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Native.POINT point);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr wp,IntPtr lp);
 [STAThread]static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new GlassVerification());}
}
