using System;
using System.Collections.Generic;
using System.Drawing;

enum PetDisplayMode
{
    Unknown,
    Single,
    Duplicate,
    Extended,
    Mixed
}

static class PetDisplayRouting
{
    // One active source can feed several physical outputs in clone mode.
    internal static PetDisplayMode Classify(IList<string> activeSources)
    {
        if (activeSources == null || activeSources.Count == 0)
            return PetDisplayMode.Unknown;
        int sources = new HashSet<string>(activeSources).Count;
        if (sources == 1)
            return activeSources.Count == 1 ? PetDisplayMode.Single : PetDisplayMode.Duplicate;
        return sources == activeSources.Count ? PetDisplayMode.Extended : PetDisplayMode.Mixed;
    }

    internal static List<PetMonitor> Normalize(IList<PetMonitor> monitors, PetDisplayMode mode)
    {
        var result = new List<PetMonitor>();
        foreach (var monitor in monitors)
        {
            var existing = result.Find(m => m.Pixels == monitor.Pixels);
            if (existing == null)
            {
                result.Add(monitor);
                continue;
            }
            if ((mode != PetDisplayMode.Duplicate && mode != PetDisplayMode.Mixed) ||
                existing.Dpi != monitor.Dpi || existing.WorkPixels != monitor.WorkPixels)
                throw new ArgumentException("Ambiguous mirrored desktop metrics");
            // Keep the primary representative even if the clone was enumerated first.
            if (monitor.Primary && !existing.Primary)
                result[result.IndexOf(existing)] = monitor;
        }
        return result;
    }
}

sealed class PetMonitor
{
    internal readonly string Id;
    internal readonly Rectangle Pixels, WorkPixels;
    internal readonly uint Dpi;
    internal readonly bool Primary;
    internal PetMonitor(string id, Rectangle pixels, Rectangle workPixels, uint dpi, bool primary)
    {
        if (String.IsNullOrEmpty(id) || dpi == 0 || pixels.Width <= 0 || pixels.Height <= 0 ||
            workPixels.Width <= 0 || workPixels.Height <= 0 || !pixels.Contains(workPixels))
            throw new ArgumentException("Invalid monitor metrics");
        Id = id;
        Pixels = pixels;
        WorkPixels = workPixels;
        Dpi = dpi;
        Primary = primary;
    }
}

sealed class PetDesktopDisplay
{
    internal readonly PetMonitor Monitor;
    internal readonly PetLogicalDisplay Logical;
    internal readonly PetDisplayCoordinates Coordinates;
    internal PetDesktopDisplay(PetMonitor monitor, Rectangle logicalBounds)
    {
        Monitor = monitor;
        Coordinates = new PetDisplayCoordinates(logicalBounds.Location, monitor.Pixels.Location, monitor.Dpi);
        // Preserve Chromium's enclosing-rectangle work-area insets.
        var original = PetDesktopSnapshot.Enclose(monitor.Pixels, monitor.Dpi);
        var work = PetDesktopSnapshot.Enclose(monitor.WorkPixels, monitor.Dpi);
        work.Offset(logicalBounds.X - original.X, logicalBounds.Y - original.Y);
        Logical = new PetLogicalDisplay(monitor.Id, logicalBounds, work);
    }
}

// Build the connected logical desktop from monitor adjacency. This implements
// the edge/offset rules in Chromium's scaling_util, without an Electron runtime.
// Ambiguous overlapping logical layouts are rejected instead of guessed.
sealed class PetDesktopSnapshot
{
    internal readonly List<PetDesktopDisplay> Displays;
    internal readonly List<PetLogicalDisplay> LogicalDisplays;
    internal readonly PetDisplayMode Mode;
    internal PetDesktopSnapshot(IList<PetMonitor> monitors, PetDisplayMode mode = PetDisplayMode.Unknown)
    {
        if (monitors == null || monitors.Count == 0)
            throw new ArgumentException("No displays");
        var remaining = PetDisplayRouting.Normalize(monitors, mode);
        Mode = mode == PetDisplayMode.Unknown
            ? (remaining.Count == 1 ? PetDisplayMode.Single : PetDisplayMode.Extended) : mode;
        if ((Mode == PetDisplayMode.Single || Mode == PetDisplayMode.Duplicate) && remaining.Count != 1)
            throw new ArgumentException("Display mode and desktop geometry disagree");
        var primary = remaining.Find(m => m.Primary);
        if (primary == null)
            throw new ArgumentException("No primary display");
        var root = new PetDesktopDisplay(primary, new Rectangle(Point.Empty, Enclose(primary.Pixels, primary.Dpi).Size));
        Displays = new List<PetDesktopDisplay>
        {
            root
        };
        remaining.Remove(primary);
        var parents = new Stack<PetDesktopDisplay>();
        parents.Push(root);
        while (parents.Count > 0)
        {
            var parent = parents.Pop();
            foreach (var child in new List<PetMonitor>(remaining))
            {
                int side = TouchingSide(parent.Monitor.Pixels, child.Pixels);
                if (side < 0)
                    continue;
                var placed = new PetDesktopDisplay(child, Place(parent, child, side));
                Displays.Add(placed);
                parents.Push(placed);
                remaining.Remove(child);
            }
        }

        // Disconnected or overlapping topologies require a verified
        // platform mapping. Do not silently fall back to global DPI scaling.
        if (remaining.Count != 0)
            throw new ArgumentException("Unsupported disconnected or mirrored desktop");
        for (int i = 0; i < Displays.Count; i++)
            for (int j = i + 1; j < Displays.Count; j++)
                if (Displays[i].Logical.Bounds.IntersectsWith(Displays[j].Logical.Bounds))
                    throw new ArgumentException("Ambiguous overlapping logical desktop");
        LogicalDisplays = Displays.ConvertAll(d => d.Logical);
    }

    internal PetDesktopDisplay Find(string id)
    {
        return Displays.Find(d => d.Monitor.Id == id);
    }

    internal PetDesktopDisplay Nearest(Point point, bool physical)
    {
        PetDesktopDisplay best = Displays[0];
        double distance = Double.MaxValue;
        foreach (var display in Displays)
        {
            var bounds = physical ? display.Monitor.Pixels : display.Logical.Bounds;
            if (bounds.Contains(point))
                return display;
            double dx = Math.Max(bounds.Left - (double)point.X, Math.Max(0, point.X - (double)bounds.Right));
            double dy = Math.Max(bounds.Top - (double)point.Y, Math.Max(0, point.Y - (double)bounds.Bottom));
            double next = dx * dx + dy * dy;
            if (next < distance)
            {
                best = display;
                distance = next;
            }
        }

        return best;
    }

    internal static Rectangle Enclose(Rectangle rect, uint dpi)
    {
        double scale = 96.0 / dpi;
        return Rectangle.FromLTRB((int)Math.Floor(rect.Left * scale), (int)Math.Floor(rect.Top * scale),
            (int)Math.Ceiling(rect.Right * scale), (int)Math.Ceiling(rect.Bottom * scale));
    }

    // 0 top, 1 right, 2 bottom, 3 left; corner precedence matches Chromium.
    static int TouchingSide(Rectangle parent, Rectangle child)
    {
        int left = Math.Max(parent.Left, child.Left), top = Math.Max(parent.Top, child.Top);
        int right = Math.Min(parent.Right, child.Right), bottom = Math.Min(parent.Bottom, child.Bottom);
        if (left == right && top == bottom)
            return parent.Bottom == top ? 2 : parent.Left == left ? 3 : 0;
        if (left == right && parent.Top <= child.Bottom && child.Top <= parent.Bottom)
            return parent.Left == left ? 3 : 1;
        if (top == bottom && parent.Left <= child.Right && child.Left <= parent.Right)
            return parent.Top == top ? 0 : 2;
        return -1;
    }

    static Rectangle Place(PetDesktopDisplay parent, PetMonitor child, int side)
    {
        bool horizontal = side == 0 || side == 2;
        var p = parent.Monitor.Pixels;
        var c = child.Pixels;
        int start = horizontal ? p.Left : p.Top;
        int length = horizontal ? p.Width : p.Height;
        int begin = (horizontal ? c.Left : c.Top) - start;
        int end = (horizontal ? c.Right : c.Bottom) - start;
        bool fromEnd = false;
        int offset;
        if (end == length && begin != 0)
        {
            fromEnd = true;
            offset = 0;
        }
        else if (begin >= 0 && begin <= length)
            offset = (int)Math.Floor(begin * 96.0 / parent.Monitor.Dpi);
        else if (end >= 0 && end <= length)
        {
            fromEnd = true;
            offset = (int)Math.Floor((length - end) * 96.0 / parent.Monitor.Dpi);
        }
        else
            offset = (int)Math.Floor(begin * 96.0 / child.Dpi);
        var size = Enclose(c, child.Dpi).Size;
        var logical = parent.Logical.Bounds;
        int parentLength = horizontal ? logical.Width : logical.Height;
        int childLength = horizontal ? size.Width : size.Height;
        if (fromEnd)
            offset = parentLength - offset - childLength;
        offset = Math.Max(-childLength, Math.Min(parentLength, offset));
        var origin = logical.Location;
        switch (side)
        {
            case 0:
                origin.Offset(offset, -size.Height);
                break;
            case 1:
                origin.Offset(logical.Width, offset);
                break;
            case 2:
                origin.Offset(offset, logical.Height);
                break;
            case 3:
                origin.Offset(-size.Width, offset);
                break;
        }

        return new Rectangle(origin, size);
    }
}
