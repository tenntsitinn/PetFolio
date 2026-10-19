using System;
using System.Drawing;

static class PetDisplayCoordinatesTests
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
        var primary = new PetDisplayCoordinates(Point.Empty, Point.Empty, 144);
        Check(primary.ToPhysical(new Point(600, 400)) == new Point(900, 600), "Primary screen preserves existing scaling");
        var right = new PetDisplayCoordinates(new Point(1920, 0), new Point(1920, 0), 144);
        Check(right.ToPhysical(new Point(2020, 200)) == new Point(2070, 300), "Right screen origin must not be scaled");
        var left = new PetDisplayCoordinates(new Point(-1280, -100), new Point(-1920, -150), 144);
        Check(left.ToPhysical(new Point(-1180, 100)) == new Point(-1770, 150), "Negative and vertically offset origins map locally");
        var above = new PetDisplayCoordinates(new Point(200, -864), new Point(300, -1080), 120);
        Check(above.ToPhysical(new Point(280, -784)) == new Point(400, -980), "Screen above primary uses both origins");
        Check(right.ToPhysicalLength(112) == 168 && left.ToPhysicalLength(112) == 168, "Lengths are independent of origins");
        foreach (uint dpi in new uint[]
        {
            96,
            120,
            144,
            168,
            192
        })
        {
            var mapping = new PetDisplayCoordinates(new Point(-1500, 200), new Point(-1920, 300), dpi);
            for (int x = -1600; x < -1300; x++)
            {
                var logical = new Point(x, x + 1800);
                Check(mapping.ToLogical(mapping.ToPhysical(logical)) == logical, "Integer DIP round trip at supported scales");
            }
        }

        bool rejected = false;
        try
        {
            new PetDisplayCoordinates(Point.Empty, Point.Empty, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Check(rejected, "Invalid DPI cannot become a mapping");
        Console.WriteLine(checks + " display coordinate checks passed");
    }
}
