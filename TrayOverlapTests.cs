using System;
using System.Drawing;
using System.Windows.Forms;

// Real popup and layered windows; no cursor movement or synthetic global input.
static class TrayOverlapTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            Run();
            Console.WriteLine(checks + " tray overlap checks passed");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(e.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        using (var host = new Form())
        using (var bubble = new QuotaLabel("", true))
        using (var menu = new ContextMenuStrip())
        using (var style = new TrayMenuStyle())
        using (var timer = new Timer())
        {
            host.StartPosition = FormStartPosition.Manual;
            host.Bounds = new Rectangle(400, 250, 500, 350);
            host.ShowInTaskbar = false;
            bool? dark = false;
            style.ReadTheme = () => dark;
            style.RefreshTheme();
            menu.Items.AddRange(bubble.Commands);
            style.Apply(menu);
            menu.Opening += (s, e) => bubble.SetTrayMenuOpen(true);
            menu.Closed += (s, e) => host.BeginInvoke(new Action(() => bubble.SetTrayMenuOpen(menu.Visible)));
            var position = new Point(500, 350);
            var pointer = new Point(560, 380);
            bubble.ReadHoverPointer = () => pointer;
            var appearance = (ToolStripMenuItem)menu.Items[0];
            int stage = 0, cycle = 0;
            var size = bubble.Size;
            host.Shown += (s, e) =>
            {
                bubble.Show();
                bubble.PreviewGlass(Color.FromArgb(230, 32, 32, 32), position);
                timer.Interval = 100;
                timer.Start();
            };
            timer.Tick += (s, e) =>
            {
                switch (stage++)
                {
                    case 0:
                        bubble.PreviewHover(pointer);
                        Check(bubble.DismissButton.Visible, "Hover button is visible before opening the menu");
                        menu.Show(new Point(520, 340));
                        break;
                    case 1:
                        bubble.PreviewHover(pointer);
                        Check(menu.Visible && !bubble.DismissButton.Visible, "Overlapping hover cannot dismiss the root menu or show the button");
                        appearance.ShowDropDown();
                        break;
                    case 2:
                        var menuSize = menu.Size;
                        var font = menu.Font;
                        dark = !style.Dark;
                        style.RefreshTheme();
                        Check(style.Dark == dark && menu.BackColor == appearance.DropDown.BackColor, "Changing theme recolours root and submenu together");
                        Check(menu.Size == menuSize && menu.Font == font, "Theme changes preserve menu geometry and font");
                        dark = null;
                        style.RefreshTheme();
                        Check(menu.BackColor == appearance.DropDown.BackColor, "Failed theme reading retains both palettes");
                        bubble.UpdatePet(new PetState(new IntPtr(1), new Point(630 + cycle, 420), 112, true, true, false, "overlap", Color.Gold));
                        bubble.PreviewHover(pointer);
                        bubble.RefreshCurrent();
                        Check(bubble.Location == position && bubble.Size == size, "Pet movement does not move the bubble over the open menu");
                        Check(menu.Visible && appearance.DropDown.Visible && !bubble.DismissButton.Visible, "Refresh and hover preserve the complete submenu chain");
                        menu.Close();
                        break;
                    case 3:
                        Check(!menu.Visible && !appearance.DropDown.Visible, "Closing root also closes submenu");
                        bubble.PreviewHover(pointer);
                        Check(bubble.DismissButton.Visible, "Hover behavior resumes after the menu closes");
                        if (++cycle == 6)
                        {
                            timer.Stop();
                            host.Close();
                        }
                        else
                            stage = 0;
                        break;
                }
            };
            Application.Run(host);
        }
    }
}
