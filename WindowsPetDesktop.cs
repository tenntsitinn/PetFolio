using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

static class WindowsPetDesktop
{
    // Enumerate directly: no WinForms Screen cache can survive a layout change.
    internal static PetDesktopSnapshot Read(IntPtr target, uint targetDpi)
    {
        int activeSourceCount;
        var mode = ReadMode(out activeSourceCount);
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
        monitors = PetDisplayRouting.Normalize(monitors, mode);
        if (activeSourceCount > 0 && monitors.Count != activeSourceCount)
            throw new InvalidOperationException("Display configuration changed during enumeration");
        // Preserve the existing single-screen DPI query/failure behavior.
        // On multiple screens, the foreign HWND DPI can lag behind the pointer.
        if (monitors.Count == 1 && target != IntPtr.Zero && targetDpi > 0)
        {
            var monitor = monitors[0];
            monitors[0] = new PetMonitor(monitor.Id, monitor.Pixels, monitor.WorkPixels, targetDpi, monitor.Primary);
        }

        return new PetDesktopSnapshot(monitors, mode);
    }

    internal static PetDisplayMode ReadMode(out int sourceCount)
    {
        sourceCount = 0;
        const uint activePaths = 2; // QDC_ONLY_ACTIVE_PATHS, not the persisted topology.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint pathCount, modeCount;
            int error = GetDisplayConfigBufferSizes(activePaths, out pathCount, out modeCount);
            // CCD is unavailable on some remote / legacy display drivers.
            // Keep the existing monitor geometry route, without guessing clone mode.
            if (error == 50 || error == 5)
                return PetDisplayMode.Unknown;
            if (error != 0)
                throw new System.ComponentModel.Win32Exception(error);
            var paths = new DisplayPath[pathCount];
            var modes = new DisplayModeInfo[modeCount];
            error = QueryDisplayConfig(activePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == 122) // Topology changed between size query and read.
                continue;
            if (error == 50 || error == 5)
                return PetDisplayMode.Unknown;
            if (error != 0)
                throw new System.ComponentModel.Win32Exception(error);
            if (pathCount == 0)
                throw new InvalidOperationException("No active display paths");
            var sources = new List<string>();
            for (int i = 0; i < pathCount; i++)
            {
                var source = paths[i].Source;
                sources.Add(source.Adapter.High + ":" + source.Adapter.Low + ":" + source.Id);
            }
            sourceCount = new HashSet<string>(sources).Count;
            return PetDisplayRouting.Classify(sources);
        }
        throw new InvalidOperationException("Display configuration is changing");
    }

    // Native LUID has 4-byte alignment (not the alignment of a C# long).
    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterId
    {
        internal uint Low;
        internal int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplaySource
    {
        internal AdapterId Adapter;
        internal uint Id, ModeIndex, Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayTarget
    {
        internal AdapterId Adapter;
        internal uint Id, ModeIndex, Technology, Rotation, Scaling;
        internal uint RefreshNumerator, RefreshDenominator, ScanOrder;
        internal int Available;
        internal uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPath
    {
        internal DisplaySource Source;
        internal DisplayTarget Target;
        internal uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO: 16-byte header + 48-byte native union.
    // We only need source identity from paths; modes are an API output buffer.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DisplayModeInfo
    {
        [FieldOffset(0)]
        internal uint Type;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray,
        ref uint modes, [Out] DisplayModeInfo[] modeArray, IntPtr topology);

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
