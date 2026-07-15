using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class MonitorGeometry
{
    public static MonitorSnapshot? BestMonitor(RectI window, IReadOnlyList<MonitorSnapshot> monitors)
    {
        if (monitors.Count == 0)
            return null;

        return monitors
            .OrderByDescending(m => window.Intersect(m.Bounds).Area)
            .ThenByDescending(m => m.IsPrimary)
            .First();
    }

    public static RectI FitToWorkArea(RectI rectangle, RectI workArea)
    {
        var width = Math.Min(Math.Max(1, rectangle.Width), workArea.Width);
        var height = Math.Min(Math.Max(1, rectangle.Height), workArea.Height);
        var x = Math.Clamp(rectangle.X, workArea.X, workArea.Right - width);
        var y = Math.Clamp(rectangle.Y, workArea.Y, workArea.Bottom - height);
        return new RectI(x, y, width, height);
    }

    public static RectI MapBetweenMonitors(RectI rectangle, RectI sourceWorkArea, RectI targetWorkArea)
    {
        if (!sourceWorkArea.IsValid || !targetWorkArea.IsValid)
            throw new ArgumentException("Source and target work areas must be valid.");

        var relativeX = (rectangle.X - sourceWorkArea.X) / (double)sourceWorkArea.Width;
        var relativeY = (rectangle.Y - sourceWorkArea.Y) / (double)sourceWorkArea.Height;
        var mapped = new RectI(
            targetWorkArea.X + (int)Math.Round(relativeX * targetWorkArea.Width),
            targetWorkArea.Y + (int)Math.Round(relativeY * targetWorkArea.Height),
            (int)Math.Round(rectangle.Width * targetWorkArea.Width / (double)sourceWorkArea.Width),
            (int)Math.Round(rectangle.Height * targetWorkArea.Height / (double)sourceWorkArea.Height));
        return FitToWorkArea(mapped, targetWorkArea);
    }
}
