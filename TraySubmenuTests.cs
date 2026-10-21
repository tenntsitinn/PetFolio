using System;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class TraySubmenuTests
{
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    [STAThread]
    static void Main()
    {
        try { Run(); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            Environment.ExitCode = 1;
        }
    }
    static void Run()
    {
        using (var root = new ContextMenuStrip())
        using (var style = new TrayMenuStyle())
        using (var view = new QuotaLabel("", new BubbleSettings { DefaultFrostedGlass = true }, new BubbleContentState(new BubbleSettings())))
        {
            Check(!view.FrostedGlass, "Legacy Default glass must not activate");
            var appearance = (ToolStripMenuItem)view.Commands[0];
            root.Items.Add(appearance);
            root.Items.Add(new ToolStripMenuItem("Refresh"));
            root.Items.Add(new ToolStripSeparator());
            var features = new ToolStripMenuItem("Features");
            features.DropDownItems.Add("Quota remaining");
            features.DropDownItems.Add("System usage");
            root.Items.Add(features);
            style.Apply(root);
            root.Show(new Point(100, 100));
            using (var rootPreview = new Bitmap(root.Width, root.Height))
            {
                root.DrawToBitmap(rootPreview, new Rectangle(Point.Empty, root.Size));
                rootPreview.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-chevron-preview.png"));
            }
            appearance.ShowDropDown();
            Application.DoEvents();
            var submenu = appearance.DropDown;
            Check(submenu.Top == root.Top && submenu.Left == root.Right, "Appearance touches the root edge without overlap: " + root.Bounds + " / " + submenu.Bounds);
            Check(root.Width == submenu.Width, "Root and appearance menus share the same width");
            Check(submenu.Items[6].Bounds.Right <= submenu.ClientSize.Width, "Compact slider fits inside the submenu");
            var pet = (ToolStripMenuItem)submenu.Items[1];
            var click = new IntPtr((pet.Bounds.Left + pet.Bounds.Width / 2) | ((pet.Bounds.Top + pet.Bounds.Height / 2) << 16));
            SendMessage(submenu.Handle, 0x201, new IntPtr(1), click);
            SendMessage(submenu.Handle, 0x202, IntPtr.Zero, click);
            Check(root.Visible && submenu.Visible && pet.Checked, "Selection must keep the submenu visible");
            Check(submenu.Items[5].TextAlign == ContentAlignment.MiddleLeft, "Opacity label is left aligned");
            bool renderedLeft = false;
            Rectangle optionTextBounds = Rectangle.Empty;
            submenu.Renderer.RenderItemText += (sender, args) =>
            {
                if (args.Item == submenu.Items[3]) optionTextBounds = args.TextRectangle;
                if (args.Item == submenu.Items[5])
                    renderedLeft = (args.TextFormat & (TextFormatFlags.Right | TextFormatFlags.HorizontalCenter)) == 0 &&
                        args.TextRectangle.Left + args.Item.Bounds.Left == optionTextBounds.Left + submenu.Items[3].Bounds.Left;
            };
            using (var preview = new Bitmap(submenu.Width, submenu.Height))
            {
                submenu.DrawToBitmap(preview, new Rectangle(Point.Empty, submenu.Size));
                preview.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-alignment-preview.png"));
            }
            Check(renderedLeft, "Opacity starts at the same left inset as other options");
            Check(submenu.Items[3].Text == "Frosted glass", "Glass precedes opacity");
            var inside = new Point(submenu.Left + 10, submenu.Top + 10);
            style.UpdateSubmenus(inside);
            Check(submenu.Visible, "Pointer inside retains the submenu");
            var outside = new Point(-30000, -30000);
            style.UpdateSubmenus(outside);
            Check(submenu.Visible, "Pointer transition has a short grace period");
            Thread.Sleep(300);
            style.UpdateSubmenus(outside);
            Check(!submenu.Visible, "Pointer outside dismisses the submenu");
            appearance.ShowDropDown();
            submenu.Close(ToolStripDropDownCloseReason.Keyboard);
            Check(!submenu.Visible, "Keyboard dismissal still works");
            features.ShowDropDown();
            Application.DoEvents();
            Check(features.DropDown.Top == root.Top + features.Bounds.Top - root.Padding.Top &&
                features.DropDown.Left == root.Right, "Features aligns to its group and touches the root edge");
            var leftPlacement = TrayMenuStyle.SubmenuLocation(new Rectangle(800, 100, 200, 300),
                new Size(200, 240), new Rectangle(0, 0, 1024, 768), 100);
            Check(leftPlacement == new Point(600, 100), "Left-opening submenu touches the parent without overlap");
            var bottomPlacement = TrayMenuStyle.SubmenuLocation(new Rectangle(100, 600, 200, 160),
                new Size(200, 240), new Rectangle(0, 0, 1024, 768), 700);
            Check(bottomPlacement == new Point(300, 528), "Bottom edge keeps submenu inside the work area without a horizontal gap");
            root.Items.Remove(appearance);
        }
        Console.WriteLine("15 submenu interaction checks passed");
    }
}
