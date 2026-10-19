using System;
using System.Drawing;

static class PetLogicalDesktopTests
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        checks++;
    }

    static void Main()
    {
        var main = new PetLogicalDisplay("main", new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var right = new PetLogicalDisplay("right", new Rectangle(1920, 0, 1280, 720), new Rectangle(1920, 0, 1280, 680));
        var left = new PetLogicalDisplay("left", new Rectangle(-1280, -100, 1280, 720), new Rectangle(-1280, -60, 1280, 680));
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(1943, 100), right, main) == main, "Retain old screen until right margin");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(1944, 100), right, main) == right, "Right margin is exclusive");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(-24, 100), left, main) == main, "Left margin is inclusive");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(-25, 100), left, main) == left, "Cross left margin");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(1900, 100), main, right) == right, "Return crossing retains current screen");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(1895, 100), main, right) == main, "Return crossing outside margin");
        var above = new PetLogicalDisplay("above", new Rectangle(0, -720, 1280, 720), new Rectangle(0, -720, 1280, 680));
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(100, -24), above, main) == main, "Top retention boundary");
        Check(PetLogicalDesktop.SelectDragDisplay(new Point(100, -25), above, main) == above, "Top crossing");
        Check(main.PetArea.Bottom == 1080 && left.PetArea.Top == -60, "Native pet area uses work-area top and screen bottom");
        var drag = new PetLogicalDrag();
        var saved = new Point(1700, 300);
        drag.Begin(new Point(1730, 345), saved, saved, main);
        drag.Move(new Point(1943, 345), right, 112);
        Check(drag.Display == main && drag.Resolve(saved, 10) == new Point(1808, 300), "Retention constrains pet to prior display");
        drag.Move(new Point(2000, 345), right, 112);
        Check(drag.Display == right && drag.Resolve(saved, 20) == new Point(1970, 300), "Crossing keeps original DIP grab offset");
        var rightPixels = new PetDisplayCoordinates(right.Bounds.Location, new Point(1920, 0), 144);
        Check(rightPixels.ToPhysical(drag.Resolve(saved, 20)) == new Point(1995, 450), "150% output conversion does not scale desktop origin");
        drag.Move(new Point(2500, 2000), right, 112);
        Check(drag.Resolve(saved, 30) == new Point(2470, 558), "Clamp logical height and 40 DIP reserve before scaling");
        drag.Move(new Point(1800, 345), main, 112);
        Check(drag.Display == main && drag.Resolve(saved, 40) == new Point(1770, 300), "Round trip does not accumulate scaling error");
        drag.End(50);
        Check(drag.Resolve(saved, 2049) == new Point(1770, 300), "Release waits for saved DIP update");
        Check(drag.Resolve(saved, 2050) == saved, "Release timeout still expires");
        drag.Begin(new Point(1730, 345), saved, saved, main);
        drag.Move(new Point(2000, 345), right, 112);
        drag.End(3000);
        Check(drag.Resolve(new Point(1971, 301), 3010) == new Point(1971, 301), "New saved DIP anchor reconciles release");
        drag.Begin(new Point(2000, 345), new Point(1970, 300), saved, right);
        drag.RefreshDisplays(new[] { main }, main);
        drag.Move(new Point(1940, 345), main, 112);
        Check(drag.Display == main && drag.Resolve(saved, 3020) == new Point(1808, 300), "Removed display cannot retain a ghost boundary");
        var resized = new PetLogicalDisplay("main", new Rectangle(0, 0, 1600, 900), new Rectangle(40, 0, 1560, 900));
        drag.RefreshDisplays(new[] { resized }, resized);
        drag.Move(new Point(1590, 1000), resized, 112);
        Check(drag.Resolve(saved, 3030) == new Point(1488, 738), "Updated display metrics constrain next movement");
        drag.Reset();
        Check(!drag.Active && drag.Display == null && drag.Resolve(saved, 3040) == saved, "Reset clears display and release state");
        Console.WriteLine(checks + " logical desktop checks passed");
    }
}
