using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Independent popup HWND: it does not call SetTrayMenuOpen or belong to Bubble.
static class BubbleZOrderTests {
    static int checks;
    static void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;}
    static void BelowPopup(QuotaLabel bubble,Form popup) {
        var order=new List<IntPtr>();
        Native.EnumWindows((h,p)=>{order.Add(h);return true;},IntPtr.Zero);
        int popupIndex=order.IndexOf(popup.Handle);
        Check(popupIndex>=0,"Popup is in the desktop window list");
        for(Form layer=bubble;layer!=null;layer=layer.Owner)
            if(layer.Visible) {
                Check(order.IndexOf(layer.Handle)>popupIndex,"Popup stays above "+layer.Text);
                Check((Native.GetWindowLongPtr(layer.Handle,-20).ToInt64()&8)!=0,"Floating layer retains WS_EX_TOPMOST");
            }
        if(bubble.DismissButton.Visible)
            Check(order.IndexOf(bubble.DismissButton.Handle)>popupIndex,"Popup stays above the close button");
    }
    [STAThread] static void Main() {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try {Run();Console.WriteLine(checks+" bubble z-order checks passed");}
        catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Run() {
        using(var bubble=new QuotaLabel("",true))using(var popup=new Form()) {
            popup.Text="Independent application flyout";popup.FormBorderStyle=FormBorderStyle.None;
            popup.ShowInTaskbar=false;popup.TopMost=true;popup.StartPosition=FormStartPosition.Manual;
            popup.Bounds=new Rectangle(350,200,800,600);
            bubble.Show();bubble.PreviewGlass(Color.FromArgb(230,32,32,32),new Point(500,350));
            Application.DoEvents();
            bubble.PreviewHover(new Point(560,380));
            Check(bubble.DismissButton.Visible,"Exposed bubble shows the close button");
            popup.Show();Application.DoEvents();
            for(int cycle=0;cycle<6;cycle++) {
                BelowPopup(bubble,popup);
                bubble.PreviewHover(new Point(bubble.Left+60,bubble.Top+30));
                Check(!bubble.DismissButton.Visible,"Covered bubble does not reveal its close button");
                var previous=bubble.Location;
                bubble.UpdatePet(new PetState(new IntPtr(1),new Point(600+cycle*8,440),112,true,true,false,"z-order",Color.Gold));
                Check(bubble.Location!=previous,"Bubble continues following the pet under the popup");
                bubble.RefreshCurrent();Application.DoEvents();
                BelowPopup(bubble,popup);
                var pointer=new Native.POINT {X=bubble.Left+60,Y=bubble.Top+30};
                Check(Native.WindowFromPoint(pointer)==popup.Handle,"Popup receives input over the bubble");
                bubble.PreviewHover(new Point(pointer.X,pointer.Y));
                Check(!bubble.DismissButton.Visible,"Following and refreshing do not raise the hover button");
                // Force the glass shadow to hide and reappear beneath the popup.
                bubble.SetDefaultAppearance(cycle%2==0);Application.DoEvents();
                BelowPopup(bubble,popup);
            }
            popup.Hide();Application.DoEvents();
            bubble.PreviewHover(new Point(bubble.Left+60,bubble.Top+30));
            Check(bubble.Visible && bubble.BubbleRequested && bubble.DismissButton.Visible,
                "Bubble interaction resumes when the popup closes without changing visibility preference");
        }
    }
}
