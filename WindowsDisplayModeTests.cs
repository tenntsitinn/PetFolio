using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class WindowsDisplayModeTests
{
    static void Main()
    {
        if (Marshal.SizeOf(typeof(WindowsPetDesktop.DisplaySource)) != 20 ||
            Marshal.SizeOf(typeof(WindowsPetDesktop.DisplayTarget)) != 48 ||
            Marshal.SizeOf(typeof(WindowsPetDesktop.DisplayPath)) != 72 ||
            Marshal.SizeOf(typeof(WindowsPetDesktop.DisplayModeInfo)) != 64)
            throw new Exception("DisplayConfig ABI does not match Windows");
        var watch = Stopwatch.StartNew();
        int sources;
        var mode = WindowsPetDesktop.ReadMode(out sources);
        watch.Stop();
        if (mode != PetDisplayMode.Unknown && sources < 1)
            throw new Exception("Active display mode has no sources");
        var desktop = WindowsPetDesktop.Read(IntPtr.Zero, 96);
        if (desktop.Displays.Count < 1)
            throw new Exception("Native display routing returned no desktop");
        Console.WriteLine("Native mode=" + mode + ", sources=" + sources +
            ", desktops=" + desktop.Displays.Count + ", mode query=" + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms; ABI and routing passed");
    }
}
