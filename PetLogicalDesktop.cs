using System;
using System.Collections.Generic;
using System.Drawing;

// Platform-neutral subset of the native Windows pet layout. All geometry is
// DIP. The Win32 adapter must supply the same nearest-display decision and
// logical desktop as Electron; monitor pixel rectangles are not valid inputs.
sealed class PetLogicalDisplay
{
    internal readonly string Id;
    internal readonly Rectangle Bounds;
    internal readonly Rectangle WorkArea;
    internal PetLogicalDisplay(string id, Rectangle bounds, Rectangle workArea)
    {
        if (String.IsNullOrEmpty(id) || bounds.Width <= 0 || bounds.Height <= 0 ||
            workArea.Width <= 0 || workArea.Height <= 0 || !bounds.Contains(workArea))
            throw new ArgumentException("A display requires an identity and valid logical bounds/work area.");
        Id = id;
        Bounds = bounds;
        WorkArea = workArea;
    }

    internal Rectangle PetArea
    {
        // Native Wp: work-area top/left/right, but full display bottom.
        get
        {
            return Rectangle.FromLTRB(WorkArea.Left, WorkArea.Top, WorkArea.Right, Bounds.Bottom);
        }
    }
}

static class PetLogicalDesktop
{
    internal const int DragDisplayMargin = 24;
    internal static PetLogicalDisplay SelectDragDisplay(Point pointer, PetLogicalDisplay nearest,
        PetLogicalDisplay previous)
    {
        if (nearest == null)
            throw new ArgumentNullException("nearest");
        if (previous == null || previous.Bounds == nearest.Bounds)
            return nearest;
        var retained = previous.Bounds;
        retained.Inflate(DragDisplayMargin, DragDisplayMargin);
        // Native ele / Ip: inclusive left/top, exclusive right/bottom.
        return retained.Contains(pointer) ? previous : nearest;
    }
}

// Logical input and logical output throughout. Reuse the external-window
// release reconciliation; DPI conversion belongs only at the platform boundary.
sealed class PetLogicalDrag
{
    readonly PetDragFollower follower = new PetDragFollower();
    internal PetLogicalDisplay Display { get; private set; }

    internal bool Active
    {
        get
        {
            return follower.Active;
        }
    }

    internal bool HasPosition
    {
        get
        {
            return follower.HasPosition;
        }
    }

    internal void Begin(Point pointer, Point renderedAnchor, Point savedAnchor, PetLogicalDisplay display)
    {
        if (display == null)
            throw new ArgumentNullException("display");
        Display = display;
        follower.Begin(pointer, renderedAnchor, savedAnchor);
    }

    internal void Move(Point pointer, PetLogicalDisplay nearest, int mascotWidth)
    {
        if (!Active)
            return;
        if (mascotWidth <= 0)
            throw new ArgumentOutOfRangeException("mascotWidth");
        Display = PetLogicalDesktop.SelectDragDisplay(pointer, nearest, Display);
        // Existing product reserve: 8 DIP bottom space + 32 DIP controls.
        follower.Move(pointer, Display.PetArea, mascotWidth, 40);
    }

    internal void RefreshDisplays(IList<PetLogicalDisplay> displays, PetLogicalDisplay nearest)
    {
        if (Display == null)
            return;
        // Retain identity, but replace stale metrics. An unplugged display must
        // not keep the 24 DIP retention rectangle alive on the next Move.
        foreach (var display in displays)
        {
            if (display.Id == Display.Id)
            {
                Display = display;
                return;
            }
        }

        Display = nearest;
    }

    internal Point Resolve(Point savedAnchor, long now)
    {
        return follower.Resolve(savedAnchor, now);
    }

    internal void End(long now)
    {
        follower.End(now);
    }

    internal void Reset()
    {
        follower.Reset();
        Display = null;
    }
}
