using System;
using System.Drawing;

static class PetDesktopSnapshotTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static PetMonitor Monitor(string id, Rectangle pixels, uint dpi, bool primary = false)
    {
        return new PetMonitor(id, pixels, pixels, dpi, primary);
    }

    static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    static void Run()
    {
        var primary = Monitor("main", new Rectangle(0, 0, 1920, 1080), 96, true);
        var right = Monitor("right", new Rectangle(1920, 0, 1920, 1080), 144);
        var desktop = new PetDesktopSnapshot(new[] { right, primary });
        Check(desktop.Find("main").Logical.Bounds == primary.Pixels, "Primary is found independently of enumeration order");
        Check(desktop.Find("right").Logical.Bounds == new Rectangle(1920, 0, 1280, 720), "Right display keeps adjacency after scaling");
        Check(desktop.Find("right").Coordinates.ToPhysical(new Point(2020, 200)) == new Point(2070, 300), "Screen-local output mapping");
        Check(desktop.Find("right").Coordinates.PointerToLogical(new Point(2071, 301)) == new Point(2020, 200), "Pointer conversion floors fractional DIP");
        Check(desktop.Nearest(new Point(1920, 100), true).Monitor.Id == "right", "Physical seam belongs to right screen");
        Check(desktop.Nearest(new Point(1920, 100), false).Monitor.Id == "right", "Logical seam belongs to right screen");
        Check(desktop.Nearest(new Point(3300, 100), false).Monitor.Id == "right", "Outside desktop uses nearest logical display");
        var left = Monitor("left", new Rectangle(-1920, 0, 1920, 1080), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, left });
        Check(desktop.Find("left").Logical.Bounds == new Rectangle(-1280, 0, 1280, 720), "Left display has a negative logical origin");
        Check(desktop.Find("left").Coordinates.ToPhysical(new Point(-1180, 100)) == new Point(-1770, 150), "Negative screen mapping");
        var above = Monitor("above", new Rectangle(0, -1080, 1920, 1080), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, above });
        Check(desktop.Find("above").Logical.Bounds == new Rectangle(0, -720, 1280, 720), "Above display uses negative logical Y");
        var offset = Monitor("offset", new Rectangle(1920, 180, 1920, 900), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, offset });
        Check(desktop.Find("offset").Logical.Bounds == new Rectangle(1920, 480, 1280, 600), "Bottom alignment takes precedence over scaling top offset");
        offset = Monitor("offset", new Rectangle(1920, 200, 1920, 1080), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, offset });
        Check(desktop.Find("offset").Logical.Bounds.Y == 200, "Offset inside parent uses parent scale");
        var scaledPrimary = Monitor("main", primary.Pixels, 144, true);
        desktop = new PetDesktopSnapshot(new[] { scaledPrimary, offset });
        Check(desktop.Find("offset").Logical.Bounds.Location == new Point(1280, 133), "Scaled primary affects origin and parent offset");
        offset = Monitor("offset", new Rectangle(1920, -200, 1920, 1080), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, offset });
        Check(desktop.Find("offset").Logical.Bounds.Y == 159, "End reference preserves the enclosing 721 DIP height at a fractional origin");
        offset = Monitor("offset", new Rectangle(1920, -300, 1920, 1800), 144);
        desktop = new PetDesktopSnapshot(new[] { primary, offset });
        Check(desktop.Find("offset").Logical.Bounds.Y == -200, "Child spanning parent uses child scale");
        var third = Monitor("third", new Rectangle(3840, 0, 1920, 1080), 192);
        desktop = new PetDesktopSnapshot(new[] { third, primary, right });
        Check(desktop.Find("third").Logical.Bounds == new Rectangle(3200, 0, 960, 540), "Three-screen chain follows parent adjacency");
        var work = new PetMonitor("right", right.Pixels, new Rectangle(1920, 60, 1920, 1020), 144, false);
        desktop = new PetDesktopSnapshot(new[] { primary, work });
        Check(desktop.Find("right").Logical.WorkArea == new Rectangle(1920, 40, 1280, 680), "Taskbar inset maps with the screen");
        Check(desktop.Find("right").Logical.PetArea.Bottom == 720, "Pet area extends to monitor bottom");
        bool rejected = false;
        try
        {
            new PetDesktopSnapshot(new[] { primary, Monitor("gap", new Rectangle(2000, 0, 1920, 1080), 144) });
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Check(rejected, "Disconnected topology is explicit, not guessed");
        Check(PetDisplayRouting.Classify(new string[0]) == PetDisplayMode.Unknown, "No paths is not mistaken for single-screen");
        Check(PetDisplayRouting.Classify(new[] { "a:0" }) == PetDisplayMode.Single, "Only one active output uses single-screen routing");
        Check(PetDisplayRouting.Classify(new[] { "a:0", "a:0" }) == PetDisplayMode.Duplicate, "Two outputs sharing a source are clones");
        Check(PetDisplayRouting.Classify(new[] { "a:0", "a:1" }) == PetDisplayMode.Extended, "Distinct sources use extended routing");
        Check(PetDisplayRouting.Classify(new[] { "a:0", "b:0" }) == PetDisplayMode.Extended, "Source IDs are scoped by adapter");
        Check(PetDisplayRouting.Classify(new[] { "a:0", "a:0", "a:1" }) == PetDisplayMode.Mixed, "Clone plus extension has mixed routing");
        var clone = Monitor("clone", primary.Pixels, 96);
        desktop = new PetDesktopSnapshot(new[] { clone, primary }, PetDisplayMode.Duplicate);
        Check(desktop.Displays.Count == 1 && desktop.Displays[0].Monitor.Id == "main", "Clone rectangles collapse to the primary desktop");
        Check(desktop.Mode == PetDisplayMode.Duplicate && desktop.Nearest(new Point(700, 300), false).Coordinates.ToPhysical(new Point(700, 300)) == new Point(700, 300),
            "Duplicate mode keeps one unshifted coordinate space");
        desktop = new PetDesktopSnapshot(new[] { primary }, PetDisplayMode.Duplicate);
        Check(desktop.Displays.Count == 1 && desktop.Mode == PetDisplayMode.Duplicate, "Windows can expose clone outputs as one monitor");
        desktop = new PetDesktopSnapshot(new[] { primary, clone, right }, PetDisplayMode.Mixed);
        Check(desktop.Displays.Count == 2 && desktop.Find("right").Logical.Bounds.Left == 1920, "Mixed mode deduplicates clones without losing extension");
        foreach (var invalid in new[] {
            Monitor("different-dpi", primary.Pixels, 144),
            new PetMonitor("different-work", primary.Pixels, new Rectangle(0, 0, 1920, 1000), 96, false)
        })
        {
            rejected = false;
            try { new PetDesktopSnapshot(new[] { primary, invalid }, PetDisplayMode.Duplicate); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Conflicting clone metrics remain explicit: " + invalid.Id);
        }
        rejected = false;
        try { new PetDesktopSnapshot(new[] { primary, clone }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unverified overlap is not assumed to be cloning");
        Console.WriteLine(checks + " desktop snapshot checks passed");
    }
}
