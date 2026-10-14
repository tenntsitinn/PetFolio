using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCENTPOLICY
    {
        public int State, Flags, GradientColor, AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct COMPOSITIONDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int Size;
    }

    [DllImport("user32.dll")]
    internal static extern bool SetWindowCompositionAttribute(IntPtr h, ref COMPOSITIONDATA data);
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumProc fn, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr h, StringBuilder name, int count);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr h, out RECT rect);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr h, uint command);
    // Showing or hiding an owned surface can raise its owners without activation.
    // Keep the complete Bubble group at its previous place in desktop z-order.
    internal static void ShowPreservingOrder(Form window)
    {
        SetVisiblePreservingOrder(window, true);
    }

    internal static void HidePreservingOrder(Form window)
    {
        SetVisiblePreservingOrder(window, false);
    }

    static void SetVisiblePreservingOrder(Form window, bool visible)
    {
        var root = window;
        while (root.Owner != null)
            root = root.Owner;
        var family = new HashSet<IntPtr>();
        CollectOwnedWindows(root, family);
        IntPtr predecessor = IntPtr.Zero;
        bool found = false;
        EnumWindows((h, p) =>
        {
            if (!IsWindowVisible(h) || !family.Contains(h))
                return true;
            predecessor = GetWindow(h, 3); // GW_HWNDPREV
            while (predecessor != IntPtr.Zero && (family.Contains(predecessor) || !IsWindowVisible(predecessor)))
                predecessor = GetWindow(predecessor, 3);
            // Inserting after an ordinary window would remove WS_EX_TOPMOST.
            if ((GetWindowLongPtr(h, -20).ToInt64() & 8) != 0 &&
                (predecessor == IntPtr.Zero || (GetWindowLongPtr(predecessor, -20).ToInt64() & 8) == 0))
                predecessor = new IntPtr(-1); // HWND_TOPMOST
            found = true;
            return false;
        }, IntPtr.Zero);
        if (visible)
            window.Show();
        else
            window.Hide();
        if (!found)
            return;
        var layers = new List<IntPtr>();
        EnumWindows((h, p) =>
        {
            if (IsWindowVisible(h) && family.Contains(h))
                layers.Add(h);
            return true;
        }, IntPtr.Zero);
        foreach (var layer in layers)
        {
            SetWindowPos(layer, predecessor, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0200);
            predecessor = layer;
        }
    }

    static void CollectOwnedWindows(Form window, HashSet<IntPtr> family)
    {
        if (window.IsHandleCreated)
            family.Add(window.Handle);
        foreach (var owned in window.OwnedForms)
            CollectOwnedWindows(owned, family);
    }

    [DllImport("user32.dll")]
    internal static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLEND
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UpdateLayeredWindow(IntPtr h, IntPtr screen, ref POINT pos, ref SIZE size, IntPtr source, ref POINT origin, uint key, ref BLEND blend, uint flags);
}
