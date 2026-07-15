namespace Thalamus.Core.Models;

public sealed record NativeWindowCandidate(
    long Handle,
    bool IsVisible,
    bool IsTopLevel,
    bool IsCloaked,
    bool IsToolWindow,
    bool IsOwned,
    bool HasNoActivateStyle,
    string ClassName,
    string Title,
    RectI Bounds,
    int ProcessId);

public sealed record WindowSnapshot(
    long Handle,
    int ProcessId,
    string ApplicationId,
    string ClassName,
    string Title,
    RectI Bounds,
    string MonitorDeviceName,
    bool IsMinimized,
    bool IsMaximized,
    bool CanClose = true);

public sealed record WindowGroup(string ApplicationId, IReadOnlyList<WindowSnapshot> Windows);

public sealed record WindowPlacementRecord(
    string ApplicationId,
    string ClassName,
    int Ordinal,
    RectI Bounds,
    string MonitorDeviceName,
    bool WasMaximized);

public sealed record LayoutProfile(
    int Version,
    string Name,
    DateTimeOffset SavedAtUtc,
    IReadOnlyList<MonitorSnapshot> Monitors,
    IReadOnlyList<WindowPlacementRecord> Windows)
{
    public const int CurrentVersion = 1;
}

public sealed record PlannedPlacement(long Handle, RectI Bounds, bool MaximizeAfterPlacement);
