using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

static class WindowsPetDesktop
{
    // Enumerate directly: no WinForms Screen cache can survive a layout change.
    internal static PetDesktopSnapshot Read(IntPtr target, uint targetDpi)
    {
        var monitors = new List<PetMonitor>();
        bool failed = false;
        MonitorProc callback = (monitor, hdc, rect, data) =>
        {
            var info = new MonitorInfo
            {
                Size = Marshal.SizeOf(typeof(MonitorInfo))
            };
            uint x, y;
            if (!GetMonitorInfo(monitor, ref info) || GetDpiForMonitor(monitor, 0, out x, out y) != 0 || x == 0)
            {
                failed = true;
                return false;
            }

            monitors.Add(new PetMonitor(info.Device, Rect(info.Bounds), Rect(info.Work),
                x, (info.Flags & 1) != 0));
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || failed)
            throw new InvalidOperationException("Monitor metrics unavailable");
        // Preserve the existing single-screen DPI query/failure behavior.
        // On multiple screens, the foreign HWND DPI can lag behind the pointer.
        if (monitors.Count == 1 && target != IntPtr.Zero && targetDpi > 0)
        {
            var monitor = monitors[0];
            monitors[0] = new PetMonitor(monitor.Id, monitor.Pixels, monitor.WorkPixels, targetDpi, monitor.Primary);
        }

        return new PetDesktopSnapshot(monitors);
    }

    static Rectangle Rect(RectangleNative r)
    {
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RectangleNative
    {
        internal int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        internal int Size;
        internal RectangleNative Bounds, Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Device;
    }

    delegate bool MonitorProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
}
