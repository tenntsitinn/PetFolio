using System;
using System.Drawing;

// A mapping requires both origins from the same display. Do not infer an
// Electron DIP origin by dividing the global physical origin by its DPI.
sealed class PetDisplayCoordinates
{
    readonly Point logicalOrigin;
    readonly Point physicalOrigin;
    readonly uint dpi;
    internal PetDisplayCoordinates(Point logicalOrigin, Point physicalOrigin, uint dpi)
    {
        if (dpi == 0)
            throw new ArgumentOutOfRangeException("dpi");
        this.logicalOrigin = logicalOrigin;
        this.physicalOrigin = physicalOrigin;
        this.dpi = dpi;
    }

    internal Point ToPhysical(Point logical)
    {
        return new Point(
            physicalOrigin.X + PetDpi.Scale(logical.X - logicalOrigin.X, 96, dpi),
            physicalOrigin.Y + PetDpi.Scale(logical.Y - logicalOrigin.Y, 96, dpi));
    }

    internal Point ToLogical(Point physical)
    {
        return new Point(
            logicalOrigin.X + PetDpi.Scale(physical.X - physicalOrigin.X, dpi, 96),
            logicalOrigin.Y + PetDpi.Scale(physical.Y - physicalOrigin.Y, dpi, 96));
    }

    internal Point PointerToLogical(Point physical)
    {
        // Chromium's Windows getCursorScreenPoint floors, rather than rounds.
        return new Point(logicalOrigin.X + (int)Math.Floor((physical.X - physicalOrigin.X) * 96.0 / dpi),
            logicalOrigin.Y + (int)Math.Floor((physical.Y - physicalOrigin.Y) * 96.0 / dpi));
    }

    internal int ToPhysicalLength(int logicalLength)
    {
        return PetDpi.Scale(logicalLength, 96, dpi);
    }
}
