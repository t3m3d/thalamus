using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class LayoutRestorePlanner
{
    public static IReadOnlyList<PlannedPlacement> Plan(
        LayoutProfile profile,
        IReadOnlyList<WindowSnapshot> currentWindows,
        IReadOnlyList<MonitorSnapshot> currentMonitors)
    {
        if (profile.Version != LayoutProfile.CurrentVersion || currentMonitors.Count == 0)
            return [];

        var currentByIdentity = currentWindows
            .GroupBy(w => (w.ApplicationId, w.ClassName))
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Handle).ToArray());

        var result = new List<PlannedPlacement>();
        foreach (var saved in profile.Windows)
        {
            if (!currentByIdentity.TryGetValue((saved.ApplicationId, saved.ClassName), out var matches) ||
                saved.Ordinal < 0 || saved.Ordinal >= matches.Length)
                continue;

            var savedMonitor = profile.Monitors.FirstOrDefault(m => m.DeviceName == saved.MonitorDeviceName)
                ?? profile.Monitors.FirstOrDefault(m => saved.Bounds.Intersect(m.Bounds).Area > 0);
            var currentMonitor = currentMonitors.FirstOrDefault(m => m.DeviceName == saved.MonitorDeviceName)
                ?? currentMonitors.FirstOrDefault(m => m.IsPrimary)
                ?? currentMonitors[0];

            var bounds = savedMonitor is null
                ? MonitorGeometry.FitToWorkArea(saved.Bounds, currentMonitor.WorkArea)
                : MonitorGeometry.MapBetweenMonitors(saved.Bounds, savedMonitor.WorkArea, currentMonitor.WorkArea);

            result.Add(new PlannedPlacement(matches[saved.Ordinal].Handle, bounds, saved.WasMaximized));
        }

        return result;
    }
}
