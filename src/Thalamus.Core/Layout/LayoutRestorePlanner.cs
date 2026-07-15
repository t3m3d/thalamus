using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class LayoutRestorePlanner
{
    public static IReadOnlyList<PlannedPlacement> Plan(
        LayoutProfile profile,
        IReadOnlyList<WindowSnapshot> currentWindows,
        IReadOnlyList<MonitorSnapshot> currentMonitors)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(currentWindows);
        ArgumentNullException.ThrowIfNull(currentMonitors);
        if (profile.Version != LayoutProfile.CurrentVersion ||
            profile.Monitors is null || profile.Windows is null)
            return [];

        var usableCurrentMonitors = currentMonitors
            .Where(monitor => monitor is not null &&
                monitor.Bounds.IsValid && monitor.WorkArea.IsValid &&
                monitor.DpiX > 0 && monitor.DpiY > 0)
            .ToArray();
        if (usableCurrentMonitors.Length == 0)
            return [];

        var usableSavedMonitors = profile.Monitors
            .Where(monitor => monitor is not null &&
                monitor.Bounds.IsValid && monitor.WorkArea.IsValid &&
                monitor.DpiX > 0 && monitor.DpiY > 0)
            .ToArray();
        var currentByIdentity = currentWindows
            .Where(window => window is not null &&
                window.Handle != 0 &&
                !string.IsNullOrWhiteSpace(window.ApplicationId) &&
                !string.IsNullOrWhiteSpace(window.ClassName))
            .GroupBy(w => IdentityKey(w.ApplicationId, w.ClassName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(w => w.Handle)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<PlannedPlacement>();
        var plannedHandles = new HashSet<long>();
        foreach (var saved in profile.Windows)
        {
            if (saved is null || !saved.Bounds.IsValid)
                continue;

            if (!currentByIdentity.TryGetValue(IdentityKey(saved.ApplicationId, saved.ClassName), out var matches) ||
                saved.Ordinal < 0 || saved.Ordinal >= matches.Length)
                continue;

            var match = matches[saved.Ordinal];
            if (!plannedHandles.Add(match.Handle))
                continue;

            var savedMonitor = usableSavedMonitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, saved.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? usableSavedMonitors.FirstOrDefault(m => saved.Bounds.Intersect(m.Bounds).Area > 0);
            var currentMonitor = usableCurrentMonitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, saved.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? usableCurrentMonitors.FirstOrDefault(m => m.IsPrimary)
                ?? usableCurrentMonitors[0];

            RectI bounds;
            try
            {
                bounds = savedMonitor is null
                    ? MonitorGeometry.FitToWorkArea(saved.Bounds, currentMonitor.WorkArea)
                    : MonitorGeometry.MapBetweenMonitors(
                        saved.Bounds, savedMonitor, currentMonitor);
            }
            catch (OverflowException)
            {
                bounds = MonitorGeometry.FitToWorkArea(saved.Bounds, currentMonitor.WorkArea);
            }

            result.Add(new PlannedPlacement(match.Handle, bounds, saved.WasMaximized));
        }

        return result;
    }

    private static string IdentityKey(string applicationId, string className) =>
        $"{applicationId}\u001f{className}";
}
