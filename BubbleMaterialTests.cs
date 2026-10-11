using System;
using System.Drawing;
using System.Windows.Forms;

// Verify desktop detail through the real compositor, rather than just checking
// an appearance flag. The blank gap below the bars contains no foreground ink.
static class BubbleMaterialTests {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    [STAThread] static void Main() {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try {Run();Console.WriteLine(checks+" real bubble material checks passed");}
        catch(Exception e){Console.Error.WriteLine(e.Message);Console.Error.WriteLine(e.StackTrace);Environment.ExitCode=1;}
    }
    static void Run() {
        using(var background=new Form())using(var bubble=new QuotaLabel("",true))using(var timer=new Timer()) {
            background.FormBorderStyle=FormBorderStyle.None;background.ShowInTaskbar=false;
            background.StartPosition=FormStartPosition.Manual;background.Location=new Point(675,325);background.ClientSize=new Size(260,150);
            background.Paint+=(s,e)=>{for(int x=0;x<260;x+=12)e.Graphics.FillRectangle((x/12)%2==0?Brushes.Black:Brushes.White,x,0,12,150);};
            bubble.ReadSystemDark=()=>true;int stage=0;
            Action present=()=>{
                bubble.SetDefaultAppearance(stage!=1);
                bubble.PreviewGlass(Color.FromArgb(230,32,32,32),new Point(700,350));
            };
            background.Shown+=(s,e)=>{
                bubble.Show();Check(bubble.Owner is GlassBackdrop && ((GlassBackdrop)bubble.Owner).Ready,"Real compositor is available");
                present();timer.Interval=700;timer.Start();
            };
            timer.Tick+=(s,e)=>{
                using(var image=new Bitmap(bubble.Width,bubble.Height)) {
                    using(var g=Graphics.FromImage(image))g.CopyFromScreen(bubble.Left,bubble.Top,0,0,image.Size);
                    int contrast=Math.Abs(image.GetPixel(66,75).R-image.GetPixel(78,75).R);
                    Console.WriteLine("Material stage "+stage+": stripe contrast "+contrast);
                    if(stage==1)Check(contrast<8,"Pet colour blurs desktop stripes");
                    else Check(contrast>=18,"Default preserves sharp desktop stripes at fixed alpha");
                }
                if(++stage<3)present();else{timer.Stop();background.Close();}
            };
            Application.Run(background);
        }
    }
}
