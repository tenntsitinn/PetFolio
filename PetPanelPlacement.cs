using System;
using System.Drawing;

sealed class PetPanelPlacement
{
    internal string Preferred { get; private set; }
    internal string Effective { get; private set; }
    internal bool Active { get; private set; }
    internal bool HasMoved { get; private set; }
    internal Point DragPosition { get; private set; }

    Point pressPointer;
    Point dragOrigin;
    internal PetPanelPlacement(string preference = "right")
    {
        Preferred = Normalize(preference);
    }

    internal static string Normalize(string value)
    {
        // Existing settings used four corners. Preserve their horizontal side.
        return value == "left" || value == "top-start" || value == "bottom-start" ? "left" : "right";
    }

    internal static string SideAt(Point pointer, Rectangle pet)
    {
        return pointer.X < pet.Left + pet.Width / 2.0 ? "left" : "right";
    }

    internal void Begin(Point pointer, Point renderedPanel)
    {
        pressPointer = pointer;
        dragOrigin = renderedPanel;
        DragPosition = dragOrigin;
        Active = true;
        HasMoved = false;
    }

    internal void Move(Point pointer)
    {
        if (!Active)
            return;
        int deltaX = pointer.X - pressPointer.X;
        int deltaY = pointer.Y - pressPointer.Y;
        if (!HasMoved && Math.Abs(deltaX) < 4 && Math.Abs(deltaY) < 4)
            return;
        HasMoved = true;
        DragPosition = new Point(dragOrigin.X + deltaX, dragOrigin.Y + deltaY);
    }

    internal bool End(Point pointer, Rectangle pet)
    {
        if (!Active)
            return false;
        bool commit = HasMoved;
        if (commit)
            Effective = Preferred = SideAt(pointer, pet);
        Active = false;
        HasMoved = false;
        return commit;
    }

    internal void Cancel()
    {
        Active = false;
        HasMoved = false;
    }

    internal Point Resolve(Rectangle pet, Size panel, Rectangle workArea, int padding, bool petDragging = false)
    {
        // Codex 26.1013.3656: Kp / fle / gle, transposed from top/bottom to left/right.
        // Drag entry points reset the previous direction before scoring; idle layout retains it.
        string preferred = pet.Left + pet.Width / 2.0 < workArea.Left + workArea.Width / 2.0 ? "right" : "left";
        string previous = petDragging ? preferred : Effective ?? Preferred;
        var left = Dock("left", pet, panel);
        var right = Dock("right", pet, panel);
        double leftScore = Overflow(left, panel, workArea) + (preferred == "left" ? 0 : 32) - (previous == "left" ? 96 : 0);
        double rightScore = Overflow(right, panel, workArea) + (preferred == "right" ? 0 : 32) - (previous == "right" ? 96 : 0);
        Effective = leftScore <= rightScore ? "left" : "right";
        // Like the native layout, apply edge padding only after choosing a direction.
        return Constrain(Effective == "left" ? left : right, panel, workArea, padding);
    }

    static Point Dock(string side, Rectangle pet, Size panel)
    {
        // Center on the pet canvas, independent of the sprite's animated outline.
        return new Point(side == "left" ? pet.Left - panel.Width - 12 : pet.Right + 12,
            pet.Top + (pet.Height - panel.Height) / 2);
    }

    static double Overflow(Point position, Size panel, Rectangle area)
    {
        double left = Math.Max(0, area.Left - position.X);
        double top = Math.Max(0, area.Top - position.Y);
        double right = Math.Max(0, position.X + panel.Width - area.Right);
        double bottom = Math.Max(0, position.Y + panel.Height - area.Bottom);
        return left + top + right + bottom + (left + right) * panel.Height + (top + bottom) * panel.Width;
    }

    internal static Point Constrain(Point position, Size panel, Rectangle area, int padding)
    {
        int x = Clamp(position.X, area.Left + padding, area.Right - panel.Width - padding);
        int y = Clamp(position.Y, area.Top + padding, area.Bottom - panel.Height - padding);
        return new Point(x, y);
    }

    static int Clamp(int n, int min, int max)
    {
        return Math.Max(min, Math.Min(Math.Max(min, max), n));
    }
}

// Client badge spring: stiffness 420, damping 20, mass .7. Animate the offset
// relative to the mascot so moving the mascot never waits for the snap animation.
sealed class PetPanelSpring
{
    double x, y, vx, vy;
    long last;
    internal bool Active { get; private set; }

    internal void Begin(Point offset, long now)
    {
        x = offset.X;
        y = offset.Y;
        vx = vy = 0;
        last = now;
        Active = true;
    }

    internal void Cancel()
    {
        Active = false;
    }

    internal Point Step(Point target, long now)
    {
        if (!Active)
            return target;
        double remaining = Math.Min(.064, Math.Max(0, now - last) / 1000.0);
        last = now;
        while (remaining > 0)
        {
            double dt = Math.Min(remaining, 1.0 / 240);
            remaining -= dt;
            vx += ((target.X - x) * 420 - vx * 20) / .7 * dt;
            vy += ((target.Y - y) * 420 - vy * 20) / .7 * dt;
            x += vx * dt;
            y += vy * dt;
        }

        if (Math.Abs(target.X - x) < .5 && Math.Abs(target.Y - y) < .5 && Math.Abs(vx) < 5 && Math.Abs(vy) < 5)
        {
            Active = false;
            x = target.X;
            y = target.Y;
        }

        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }
}
