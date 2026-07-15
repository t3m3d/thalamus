using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class DpiPlacement
{
    public static RectI Scale(RectI rectangle, uint sourceDpi, uint targetDpi)
    {
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));
        if (sourceDpi == 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDpi), "DPI values must be positive.");
        if (targetDpi == 0)
            throw new ArgumentOutOfRangeException(nameof(targetDpi), "DPI values must be positive.");

        var ratio = targetDpi / (double)sourceDpi;
        var scaled = new RectI(
            Round(rectangle.X * ratio),
            Round(rectangle.Y * ratio),
            Math.Max(1, Round(rectangle.Width * ratio)),
            Math.Max(1, Round(rectangle.Height * ratio)));
        if (!scaled.IsValid)
            throw new OverflowException("The DPI-scaled rectangle exceeds the supported range.");

        return scaled;
    }

    private static int Round(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < int.MinValue || rounded > int.MaxValue)
            throw new OverflowException("The DPI-scaled coordinate exceeds the supported range.");

        return (int)rounded;
    }
}
