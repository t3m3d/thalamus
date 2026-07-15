using Thalamus.Core.Models;

namespace Thalamus.Core.Services;

public static class WindowRules
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost"
    };

    public static bool IsEligible(NativeWindowCandidate candidate) =>
        candidate.Handle != 0 &&
        candidate.IsVisible &&
        candidate.IsTopLevel &&
        !candidate.IsCloaked &&
        !candidate.IsToolWindow &&
        !candidate.IsOwned &&
        !candidate.HasNoActivateStyle &&
        !ShellClasses.Contains(candidate.ClassName) &&
        candidate.Bounds.Width >= 80 &&
        candidate.Bounds.Height >= 40 &&
        !string.IsNullOrWhiteSpace(candidate.Title);
}

public static class WindowGrouping
{
    public static IReadOnlyList<WindowGroup> Group(IEnumerable<WindowSnapshot> windows) => windows
        .GroupBy(w => string.IsNullOrWhiteSpace(w.ApplicationId) ? $"pid:{w.ProcessId}" : w.ApplicationId,
            StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => new WindowGroup(g.Key, g.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToArray()))
        .ToArray();
}
