using System;
using System.Drawing;

static class PetPanelTests
{
    static int passed;
    static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
        passed++;
    }

    static void Main()
    {
        var pet = new Rectangle(500, 400, 112, 150);
        var panel = new Size(208, 104);
        var area = new Rectangle(0, 0, 1920, 1040);
        var p = new PetPanelPlacement("invalid");
        Check(p.Preferred == "right", "invalid preference defaults to right");
        Check(new PetPanelPlacement(null).Preferred == "right", "missing preference defaults to right");
        foreach (string legacy in new[] { "top-start", "bottom-start", "left" })
            Check(new PetPanelPlacement(legacy).Preferred == "left", "left preference migration: " + legacy);
        foreach (string legacy in new[] { "top-end", "bottom-end", "right" })
            Check(new PetPanelPlacement(legacy).Preferred == "right", "right preference migration: " + legacy);
        Check(PetPanelPlacement.SideAt(new Point(555, 100), pet) == "left", "release left of center selects left");
        Check(PetPanelPlacement.SideAt(new Point(555, 900), pet) == "left", "release height does not change side");
        Check(PetPanelPlacement.SideAt(new Point(556, 100), pet) == "right", "center equality selects right");
        var initial = p.Resolve(pet, panel, area, 24);
        Check(initial == new Point(624, 423), "150px pet and 104px panel share horizontal centerline");
        Check(p.Resolve(new Rectangle(500, 400, 112, 80), panel, area, 24) == new Point(624, 388), "panel taller than pet remains centered");
        var odd = p.Resolve(new Rectangle(500, 400, 112, 151), panel, area, 24);
        Check(Math.Abs(odd.Y + panel.Height / 2.0 - 475.5) <= .5, "odd heights differ by at most half a pixel");
        p.Begin(new Point(620, 335), initial);
        p.Move(new Point(623, 338));
        Check(!p.HasMoved && p.DragPosition == initial, "3px jitter is a click");
        p.Move(new Point(624, 335));
        Check(p.HasMoved && p.DragPosition == new Point(628, 423), "4px starts drag and retains grab offset");
        p.Move(new Point(650, 360));
        Check(p.DragPosition == new Point(654, 448), "full displacement from original press avoids drift");
        p.Move(new Point(620, 335));
        Check(p.HasMoved, "returning to press after drag does not become a click");
        Check(p.End(new Point(450, 600), pet) && p.Preferred == "left", "release pointer selects side");
        var leftPosition = p.Resolve(pet, panel, area, 24);
        Check(leftPosition == new Point(280, 423), "left side uses the same centerline");
        p.Begin(new Point(340, 580), leftPosition);
        p.End(new Point(340, 580), pet);
        Check(p.Preferred == "left", "click preserves preference");
        p.Begin(new Point(340, 580), leftPosition);
        p.Move(new Point(900, 100));
        p.Cancel();
        Check(!p.Active && p.Preferred == "left", "capture loss or Escape preserves side");
        var stored = new PetPanelPlacement(p.Preferred);
        Check(stored.Resolve(pet, panel, area, 24) == leftPosition, "restoring saved side recreates position");
        var edge = stored.Resolve(new Rectangle(1750, 950, 112, 150), panel, area, 24);
        Check(edge == new Point(1530, 912) && stored.Effective == "left", "bottom constraint preserves side and keeps shadow visible");
        Check(stored.Resolve(new Rectangle(500, -30, 112, 150), panel, area, 24).Y == 24, "top edge constrains centered position");
        Check(stored.Resolve(pet, panel, area, 24) == leftPosition, "leaving edge restores vertical centering");
        stored.Resolve(new Rectangle(30, 400, 112, 150), panel, area, 24);
        Check(stored.Effective == "right" && stored.Preferred == "left", "left boundary flips without changing preference");
        Check(stored.Resolve(pet, panel, area, 24) == initial && stored.Effective == "right", "space recovery retains the effective side");
        var right = new PetPanelPlacement("right");
        right.Resolve(new Rectangle(1750, 400, 112, 150), panel, area, 24);
        Check(right.Effective == "left" && right.Preferred == "right", "right boundary flips horizontally");
        var negative = right.Resolve(new Rectangle(-1400, 400, 112, 150), panel, new Rectangle(-1920, 0, 1920, 1040), 24, true);
        Check(negative == new Point(-1276, 423), "negative monitor coordinates preserved");
        var narrow = new Rectangle(0, 0, 400, 1040);
        right.Resolve(new Rectangle(144, 400, 112, 150), panel, narrow, 24);
        Check(right.Effective == "right", "equal overflow keeps preference without oscillation");
        right.Resolve(new Rectangle(200, 400, 112, 150), panel, narrow, 24);
        Check(right.Effective == "left", "when neither side fits choose less horizontal overflow");
        Check(PetPanelPlacement.Constrain(new Point(600, 600), panel, new Rectangle(0, 0, 100, 80), 24) == new Point(24, 24), "work area smaller than panel has a deterministic position");
        var crossing = new PetPanelPlacement("left");
        crossing.Resolve(new Rectangle(903, 400, 112, 150), panel, area, 24, true);
        Check(crossing.Effective == "right", "drag at canvas center 959 selects right despite saved left");
        crossing.Resolve(new Rectangle(904, 400, 112, 150), panel, area, 24, true);
        Check(crossing.Effective == "left", "drag at exact work-area center 960 switches left immediately");
        crossing.Resolve(new Rectangle(905, 400, 112, 150), panel, area, 24, true);
        Check(crossing.Effective == "left", "drag past center keeps left");
        crossing.Resolve(new Rectangle(903, 400, 112, 150), panel, area, 24);
        Check(crossing.Effective == "left", "idle re-layout across center retains previous side");
        crossing.Resolve(new Rectangle(903, 400, 112, 150), panel, area, 24, true);
        Check(crossing.Effective == "right", "reverse drag switches right without a delay band");
        crossing.Resolve(new Rectangle(904, 400, 113, 150), panel, area, 24, true);
        Check(crossing.Effective == "left", "odd canvas width uses the fractional center");
        var offsetArea = new Rectangle(-1800, 80, 1600, 960);
        crossing.Resolve(new Rectangle(-1057, 400, 112, 150), panel, offsetArea, 24, true);
        Check(crossing.Effective == "right", "negative work-area origin and reserved left area affect midpoint");
        crossing.Resolve(new Rectangle(-1056, 400, 112, 150), panel, offsetArea, 24, true);
        Check(crossing.Effective == "left", "negative work-area exact center switches left");
        var scoring = new PetPanelPlacement("left");
        scoring.Resolve(new Rectangle(12, 0, 10, 10), new Size(1, 1), new Rectangle(0, 0, 200, 100), 0);
        Check(scoring.Effective == "left", "native retention bonus outweighs a small overflow");
        scoring.Resolve(new Rectangle(12, 0, 10, 100), new Size(1, 104), new Rectangle(0, 0, 200, 200), 0);
        Check(scoring.Effective == "right", "native area-weighted overflow outweighs retention for a tall panel");
        var above = new Rectangle(pet.Left - 110, pet.Top - 145, 330, 141);
        var below = new Rectangle(pet.Left - 110, pet.Bottom + 4, 330, 141);
        var actionBar = new Rectangle(pet.Left, pet.Bottom + 8, pet.Width, 32);
        foreach (string side in new[] { "left", "right" })
        {
            var docking = new PetPanelPlacement(side);
            var rect = new Rectangle(docking.Resolve(pet, panel, area, 24), panel);
            Check(!rect.IntersectsWith(above) && !rect.IntersectsWith(below), side + " avoids native notification trays");
            Check(!rect.IntersectsWith(actionBar) && !rect.IntersectsWith(pet), side + " clears pet and Action Bar");
        }

        var spring = new PetPanelSpring();
        spring.Begin(new Point(700, -400), 0);
        var goal = new Point(88, -100);
        Point at = Point.Empty;
        for (int now = 16; now <= 2000; now += 16)
            at = spring.Step(goal, now);
        Check(!spring.Active && at == goal, "native spring parameters converge exactly");
        spring.Begin(new Point(20, 30), 0);
        var offset = spring.Step(goal, 16);
        var oldAbsolute = new Point(500 + offset.X, 400 + offset.Y);
        var movedAbsolute = new Point(650 + offset.X, 430 + offset.Y);
        Check(movedAbsolute.X - oldAbsolute.X == 150 && movedAbsolute.Y - oldAbsolute.Y == 30, "offset animation follows mascot immediately");
        spring.Cancel();
        Check(spring.Step(goal, 32) == goal, "new drag cancels previous snap");
        Check(spring.Track(new Point(124, 23), 40) == new Point(124, 23) && !spring.Active,
            "first layout appears immediately without animating from a stale screen");
        Check(spring.Track(new Point(124, 23), 56) == new Point(124, 23) && !spring.Active,
            "unchanged offset follows pet without lag or a running spring");
        var edgeGoal = new Point(80, 23);
        Check(spring.Track(edgeGoal, 72) == new Point(124, 23) && spring.Active,
            "edge constraint starts from current relative position");
        var squeezed = spring.Track(edgeGoal, 88);
        Check(squeezed.X < 124 && squeezed.X > 80 && squeezed.Y == 23,
            "edge pressure changes position without changing vertical alignment");
        Check(spring.Track(new Point(124, 23), 88) == squeezed,
            "crossing back at the same instant preserves position");
        bool overshot = false;
        for (int now = 104; now <= 2000; now += 16)
        {
            at = spring.Track(new Point(124, 23), now);
            overshot |= at.X > 124;
        }
        Check(overshot, "release from edge has a damped rebound");
        Check(!spring.Active && at == new Point(124, 23), "edge rebound settles exactly");

        // Cross from a right-edge left dock to the next screen's left-edge right dock.
        spring.Cancel();
        spring.Track(new Point(-220, 23), 0);
        spring.Track(new Point(124, 23), 16);
        var firstCrossing = spring.Track(new Point(124, 23), 32);
        Check(firstCrossing.X > -220 && firstCrossing.X < 124, "cross-screen side change is animated");
        var nextScreen = new Rectangle(1920, 0, 1920, 1040);
        var bounded = PetPanelPlacement.Constrain(new Point(1920 + firstCrossing.X, 423), panel, nextScreen, 24);
        Check(bounded.X == 1944, "new screen edge holds the bubble while its spring catches up");
        for (int now = 48; now <= 2000; now += 16)
            at = spring.Track(new Point(124, 23), now);
        Check(at == new Point(124, 23) && !spring.Active, "cross-screen spring releases and settles");
        spring.Cancel();
        Check(spring.Track(new Point(-220, 40), 2016) == new Point(-220, 40) && !spring.Active,
            "hide or target change discards old motion");
        Console.WriteLine("Passed " + passed + " relative placement checks.");
    }
}
