using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class PetTrackingDiagnostic
{
    public string Reason, StateError;
    public int Candidates;
    public bool AnchorKnown, PetOpen, WindowVisible;
    internal static string Explain(string reason)
    {
        switch (reason)
        {
            case "ready":
                return "Pet window is bound and visible.";
            case "state-file-missing":
                return "Desktop pet state is missing. Open a Pet or check the desktop data directory.";
            case "state-read-failed":
                return "Desktop pet state could not be read. Check file access; a later poll will retry.";
            case "anchor-unavailable":
                return "Pet position is unavailable. Open or move the Pet; client configuration may have changed.";
            case "pet-closed":
                return "The desktop reports that the Pet is closed. Open the Pet in Codex.";
            case "window-not-found":
                return "No matching pet window was found. Check that Codex Pet is visible; client window details may have changed.";
            case "ambiguous-windows":
                return "Multiple matching windows were found. Close extra pet windows; binding requires a unique match.";
            case "window-hidden":
                return "The matching pet window is hidden or minimized. Restore it in Codex.";
            case "display-unavailable":
                return "Display coordinates are unavailable or this monitor layout is not supported. The bubble is hidden until a valid layout is available.";
            case "display-position-unavailable":
                return "The saved Pet display does not match the current desktop. Waiting for Codex to publish its position.";
            default:
                return "Pet discovery has not started.";
        }
    }

    internal static PetTrackingDiagnostic Create(string stateError, bool anchorKnown, bool petOpen, int candidates, bool windowVisible)
    {
        string reason = stateError == "missing" ? "state-file-missing" : stateError != null ? "state-read-failed" : !anchorKnown ? "anchor-unavailable" :
            !petOpen ? "pet-closed" : candidates == 0 ? "window-not-found" : candidates > 1 ? "ambiguous-windows" : !windowVisible ? "window-hidden" : "ready";
        return new PetTrackingDiagnostic
        {
            Reason = reason,
            StateError = stateError,
            AnchorKnown = anchorKnown,
            PetOpen = petOpen,
            Candidates = candidates,
            WindowVisible = windowVisible
        };
    }

    internal bool SameAs(PetTrackingDiagnostic other)
    {
        return other != null && Reason == other.Reason && StateError == other.StateError && AnchorKnown == other.AnchorKnown && PetOpen == other.PetOpen && Candidates == other.Candidates && WindowVisible == other.WindowVisible;
    }
}

static class PetDiagnostics
{
    internal static string WindowsVersion
    {
        get
        {
            var info = new OsVersion();
            info.Size = Marshal.SizeOf(typeof(OsVersion));
            try
            {
                if (RtlGetVersion(ref info) == 0)
                    return "Windows " + info.Major + "." + info.Minor + " (build " + info.Build + ")";
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            return Environment.OSVersion.VersionString;
        }
    }

    internal static object Capture(PetTrackingDiagnostic status, IntPtr target)
    {
        string version = CodexVersion(target);
        var displays = new List<object>();
        foreach (var screen in Screen.AllScreens)
        {
            double? scale = Scale(screen);
            displays.Add(new { device = screen.DeviceName, primary = screen.Primary,
                bounds = new { x = screen.Bounds.X, y = screen.Bounds.Y, width = screen.Bounds.Width, height = screen.Bounds.Height }, scale = scale });
        }

        return new
        {
            checkedAtUtc = DateTime.UtcNow.ToString("o"),
            reason = status.Reason,
            message = PetTrackingDiagnostic.Explain(status.Reason),
            stateError = status.StateError,
            candidateWindows = status.Candidates,
            anchorKnown = status.AnchorKnown,
            petOpen = status.PetOpen,
            windowVisible = status.WindowVisible,
            windows = WindowsVersion,
            codexVersion = version,
            petfolioVersion = typeof(Program).Assembly.GetName().Version.ToString(3),
            displays = displays
        };
    }

    static string CodexVersion(IntPtr target)
    {
        if (target != IntPtr.Zero)
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(target, out pid);
                // ChatGPT.exe's ProductVersion describes Electron, not the Codex package.
                using (var process = Process.GetProcessById((int)pid))
                    return PackageVersion(process.MainModule.FileName);
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentException)
            {
            }

        return null;
    }

    internal static string PackageVersion(string executable)
    {
        var match = Regex.Match(executable ?? "", @"OpenAI\.Codex_(\d+(?:\.\d+){2,3})_", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    static double? Scale(Screen screen)
    {
        try
        {
            var monitor = MonitorFromPoint(new Native.POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 }, 2);
            uint x, y;
            if (GetDpiForMonitor(monitor, 0, out x, out y) == 0)
                return x / 96.0;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return null;
    }

    internal static string Summary(PetTrackingDiagnostic status, IntPtr target)
    {
        string message = PetTrackingDiagnostic.Explain(status.Reason) + "\n\n" + WindowsVersion + "\nCodex version: " + (CodexVersion(target) ?? "unavailable") +
            "\nPetFolio: " + typeof(Program).Assembly.GetName().Version.ToString(3) + "\nMatching windows: " + status.Candidates;
        foreach (var screen in Screen.AllScreens)
        {
            var scale = Scale(screen);
            message += "\n" + screen.DeviceName + ": " + screen.Bounds.Width + " × " + screen.Bounds.Height +
                (scale.HasValue ? " at " + (100 * scale.Value).ToString("0") + "%" : " (scale unavailable)");
        }

        return message;
    }

    internal static void Save(string directory, PetTrackingDiagnostic status, IntPtr target)
    {
        try
        {
            var report = Capture(status, target);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "pet-diagnostic.json"), new JavaScriptSerializer().Serialize(report));
            Program.Record("pet-diagnostic", report);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static object Read(string directory)
    {
        try
        {
            return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(directory, "pet-diagnostic.json")));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OsVersion
    {
        internal int Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string ServicePack;
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    static extern int RtlGetVersion(ref OsVersion version);
    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Native.POINT point, uint flags);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
}
