using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class DpiPlacement
{
    public static RectI Scale(RectI rectangle, uint sourceDpi, uint targetDpi)
    {
        if (sourceDpi == 0 || targetDpi == 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDpi), "DPI values must be positive.");

        var ratio = targetDpi / (double)sourceDpi;
        return new RectI(
            Round(rectangle.X * ratio),
            Round(rectangle.Y * ratio),
            Math.Max(1, Round(rectangle.Width * ratio)),
            Math.Max(1, Round(rectangle.Height * ratio)));
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
