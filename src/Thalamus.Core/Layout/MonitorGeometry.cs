using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class MonitorGeometry
{
    public static MonitorSnapshot? BestMonitor(RectI window, IReadOnlyList<MonitorSnapshot> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var validMonitors = monitors
            .Where(monitor => monitor is not null &&
                monitor.Bounds.IsValid && monitor.WorkArea.IsValid)
            .ToArray();
        if (validMonitors.Length == 0)
            return null;

        return validMonitors
            .OrderByDescending(m => window.Intersect(m.Bounds).Area)
            .ThenByDescending(m => m.IsPrimary)
            .First();
    }

    public static RectI FitToWorkArea(RectI rectangle, RectI workArea)
    {
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));
        if (!workArea.IsValid)
            throw new ArgumentException("The work area must be valid.", nameof(workArea));

        var width = Math.Min(Math.Max(1, rectangle.Width), workArea.Width);
        var height = Math.Min(Math.Max(1, rectangle.Height), workArea.Height);
        var x = Math.Clamp(rectangle.X, workArea.X, workArea.Right - width);
        var y = Math.Clamp(rectangle.Y, workArea.Y, workArea.Bottom - height);
        return new RectI(x, y, width, height);
    }


    public static RectI EnsureVisible(RectI rectangle, IReadOnlyList<MonitorSnapshot> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));

        var validMonitors = monitors
            .Where(monitor => monitor.Bounds.IsValid && monitor.WorkArea.IsValid)
            .ToArray();
        if (validMonitors.Length == 0)
            return rectangle;

        var monitor = BestMonitor(rectangle, validMonitors)!;
        var titleBand = new RectI(
            rectangle.X, rectangle.Y, rectangle.Width, Math.Min(40, rectangle.Height));
        var visibleTitleBand = titleBand.Intersect(monitor.WorkArea);
        if (visibleTitleBand.Width >= Math.Min(80, titleBand.Width) &&
            visibleTitleBand.Height >= Math.Min(24, titleBand.Height))
            return rectangle;

        return FitToWorkArea(rectangle, monitor.WorkArea);
    }

    public static MonitorSnapshot? AdjacentMonitor(
        IReadOnlyList<MonitorSnapshot> monitors,
        string currentDeviceName,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(currentDeviceName);
        var ordered = monitors
            .Where(monitor => monitor.Bounds.IsValid && monitor.WorkArea.IsValid)
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length < 2)
            return null;

        var currentIndex = Array.FindIndex(ordered, monitor =>
            string.Equals(monitor.DeviceName, currentDeviceName, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return ordered.FirstOrDefault(monitor => monitor.IsPrimary) ?? ordered[0];

        var targetIndex = (int)(((long)currentIndex + offset) % ordered.Length +
            ordered.Length) % ordered.Length;
        return ordered[targetIndex];
    }

    public static RectI WorkspaceToScreen(
        RectI rectangle,
        RectI monitorBounds,
        RectI workArea)
    {
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));
        if (!monitorBounds.IsValid)
            throw new ArgumentException("The monitor bounds must be valid.", nameof(monitorBounds));
        if (!workArea.IsValid)
            throw new ArgumentException("The work area must be valid.", nameof(workArea));

        // Workspace coordinates keep the monitor's virtual-screen origin but
        // omit that monitor's local top/left appbar inset.
        var x = (long)rectangle.X + workArea.X - monitorBounds.X;
        var y = (long)rectangle.Y + workArea.Y - monitorBounds.Y;
        if (x < int.MinValue || x > int.MaxValue ||
            y < int.MinValue || y > int.MaxValue)
            throw new OverflowException("The screen coordinate exceeds the supported range.");

        var translated = new RectI(
            (int)x,
            (int)y,
            rectangle.Width,
            rectangle.Height);
        if (!translated.IsValid)
            throw new OverflowException("The screen rectangle exceeds the supported range.");

        return translated;
    }

    public static RectI MapBetweenMonitors(
        RectI rectangle,
        MonitorSnapshot sourceMonitor,
        MonitorSnapshot targetMonitor)
    {
        ArgumentNullException.ThrowIfNull(sourceMonitor);
        ArgumentNullException.ThrowIfNull(targetMonitor);
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));
        if (!sourceMonitor.WorkArea.IsValid || !targetMonitor.WorkArea.IsValid)
            throw new ArgumentException("Source and target monitor work areas must be valid.");
        if (sourceMonitor.DpiX == 0 || targetMonitor.DpiX == 0)
            throw new ArgumentException("Source and target monitor DPI values must be positive.");
        if (sourceMonitor.DpiY == 0 || targetMonitor.DpiY == 0)
            throw new ArgumentException("Source and target monitor DPI values must be positive.");

        var sourceWorkArea = sourceMonitor.WorkArea;
        var targetWorkArea = targetMonitor.WorkArea;
        var relativeX = ((long)rectangle.X - sourceWorkArea.X) / (double)sourceWorkArea.Width;
        var relativeY = ((long)rectangle.Y - sourceWorkArea.Y) / (double)sourceWorkArea.Height;
        var scaledWidth = DpiPlacement.Scale(
            new RectI(0, 0, rectangle.Width, 1),
            sourceMonitor.DpiX,
            targetMonitor.DpiX).Width;
        var scaledHeight = DpiPlacement.Scale(
            new RectI(0, 0, 1, rectangle.Height),
            sourceMonitor.DpiY,
            targetMonitor.DpiY).Height;
        var mapped = new RectI(
            RoundToInt(targetWorkArea.X + relativeX * targetWorkArea.Width),
            RoundToInt(targetWorkArea.Y + relativeY * targetWorkArea.Height),
            scaledWidth,
            scaledHeight);
        if (!mapped.IsValid)
            throw new OverflowException("The mapped monitor rectangle exceeds the supported range.");

        return FitToWorkArea(mapped, targetWorkArea);
    }

    public static RectI MapBetweenMonitors(RectI rectangle, RectI sourceWorkArea, RectI targetWorkArea)
    {
        if (!rectangle.IsValid)
            throw new ArgumentException("The rectangle must be valid.", nameof(rectangle));
        if (!sourceWorkArea.IsValid || !targetWorkArea.IsValid)
            throw new ArgumentException("Source and target work areas must be valid.");

        var relativeX = ((long)rectangle.X - sourceWorkArea.X) / (double)sourceWorkArea.Width;
        var relativeY = ((long)rectangle.Y - sourceWorkArea.Y) / (double)sourceWorkArea.Height;
        var mapped = new RectI(
            RoundToInt(targetWorkArea.X + relativeX * targetWorkArea.Width),
            RoundToInt(targetWorkArea.Y + relativeY * targetWorkArea.Height),
            Math.Max(1, RoundToInt(
                rectangle.Width * (double)targetWorkArea.Width / sourceWorkArea.Width)),
            Math.Max(1, RoundToInt(
                rectangle.Height * (double)targetWorkArea.Height / sourceWorkArea.Height)));
        if (!mapped.IsValid)
            throw new OverflowException("The mapped monitor rectangle exceeds the supported range.");

        return FitToWorkArea(mapped, targetWorkArea);
    }

    private static int RoundToInt(double value)
    {
        var rounded = Math.Round(value);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw new OverflowException("The mapped monitor coordinate exceeds the supported range.");

        return (int)rounded;
    }
}
