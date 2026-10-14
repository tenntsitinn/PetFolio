using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

// Codex 26.1007 activity material: #ffffffe6 / #202020e6,
// border token at 60%, with white inset highlights. Layout stays in BubbleLayout.
static class BubbleAppearance
{
    // Native Status Bubble --text-primary / --text-secondary tokens.
    // Solid RGB colours: Default text never inherits panel or pet-text alpha.
    static readonly Color lightText = ColorTranslator.FromHtml("#0D0D0D");
    static readonly Color darkText = ColorTranslator.FromHtml("#FFFFFF");
    static readonly Color lightSecondary = ColorTranslator.FromHtml("#5D5D5D");
    static readonly Color darkSecondary = ColorTranslator.FromHtml("#CDCDCD");
    internal static bool? ReadSystemDark()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.IO.IOException)
        {
            return null;
        }
    }

    internal static Color Surface(bool dark)
    {
        return dark ? Color.FromArgb(230, 32, 32, 32) : Color.FromArgb(230, 255, 255, 255);
    }

    internal static Color Text(bool dark)
    {
        return dark ? darkText : lightText;
    }

    internal static Color Secondary(bool dark)
    {
        return dark ? darkSecondary : lightSecondary;
    }

    internal static Color Border(bool dark)
    {
        return Color.FromArgb(dark ? 23 : 16, dark ? Color.White : Color.Black);
    }

    internal static void DrawEdge(Graphics g, GraphicsPath shape, RectangleF bounds, bool dark)
    {
        // Reproduce the shallow CSS inset highlights inside the rounded clip.
        var state = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        using (var gradient = new LinearGradientBrush(bounds, Color.White, Color.White, LinearGradientMode.Vertical))
        {
            gradient.InterpolationColors = new ColorBlend
            {
                Positions = new float[]
                {
                    0,
                    .12f,
                    .5f,
                    .88f,
                    1
                },
                Colors = new[]
                {
                    Color.FromArgb(dark ? 51 : 153, Color.White),
                    Color.Transparent,
                    Color.Transparent,
                    Color.Transparent,
                    Color.FromArgb(dark ? 20 : 51, Color.White)
                }
            };
            using (var highlight = new Pen(gradient, 2))
                g.DrawPath(highlight, shape);
        }

        g.Restore(state);
        using (var border = new Pen(Border(dark), 1))
            g.DrawPath(border, shape);
    }
}
