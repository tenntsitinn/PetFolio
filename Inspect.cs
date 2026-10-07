using System;
using System.Windows.Automation;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
class Inspect {
 [StructLayout(LayoutKind.Sequential)] struct Rect { public int left,top,right,bottom; }
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [STAThread] static void Main(string[] args) {
  if (args[0] == "capture-window") {
   var handle=new IntPtr(long.Parse(args[1])); Rect r;GetWindowRect(handle,out r);
   using(var b=new Bitmap(r.right-r.left,r.bottom-r.top)) {
    using(var g=Graphics.FromImage(b)){var dc=g.GetHdc();try { Console.WriteLine(PrintWindow(handle,dc,2)); }finally{g.ReleaseHdc(dc);} }
    var crop=new Rectangle(int.Parse(args[2])-r.left,int.Parse(args[3])-r.top,int.Parse(args[4]),int.Parse(args[5]));
    using(var c=b.Clone(crop,PixelFormat.Format32bppArgb))c.Save(args[6],ImageFormat.Png);
   }
   return;
  }
  if (args[0] == "capture") {
   int x=int.Parse(args[1]),y=int.Parse(args[2]),w=int.Parse(args[3]),h=int.Parse(args[4]);
   using(var b=new Bitmap(w,h)) { using(var g=Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,new Size(w,h));b.Save(args[5],ImageFormat.Png); }
   return;
  }
  var root=AutomationElement.FromHandle(new IntPtr(long.Parse(args[0])));
  Visit(root,0);
 }
 static void Visit(AutomationElement e,int depth) {
  if(depth>4)return;
  try {
   Console.WriteLine(new string(' ',depth*2)+e.Current.ControlType.ProgrammaticName+" | "+e.Current.Name+" | "+e.Current.AutomationId+" | "+e.Current.BoundingRectangle);
   var walker=TreeWalker.ControlViewWalker;var c=walker.GetFirstChild(e);int n=0;
   while(c!=null && n++<25){Visit(c,depth+1);c=walker.GetNextSibling(c);}
  }catch(Exception ex){Console.WriteLine(ex.GetType().Name);}
 }
}
