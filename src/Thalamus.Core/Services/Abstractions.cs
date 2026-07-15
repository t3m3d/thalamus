using Thalamus.Core.Commands;
using Thalamus.Core.Models;

namespace Thalamus.Core.Services;

public interface IWindowManager
{
    Task<IReadOnlyList<WindowSnapshot>> GetWindowsAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<MonitorSnapshot> GetMonitors();
    long GetForegroundWindow();
    void Activate(long handle);
    void Minimize(long handle);
    Task CloseAsync(long handle, CancellationToken cancellationToken = default);
    bool Tile(long handle, TileTarget target);
    bool MoveToMonitor(long handle, string monitorDeviceName);
    bool ApplyPlacement(long handle, RectI bounds, bool maximizeAfterPlacement);
}

public interface IWindowTracker : IDisposable
{
    event EventHandler? WindowsChanged;
    void Start();
}

public sealed record VirtualDesktopCapabilities(
    bool CanQueryMembership,
    bool CanMoveToKnownDesktop,
    bool CanSwitchAdjacent,
    string Explanation);

public interface IVirtualDesktopService
{
    VirtualDesktopCapabilities Capabilities { get; }
    Task<bool> SwitchAsync(Direction direction, CancellationToken cancellationToken = default);
    Task<bool> MoveWindowAsync(long handle, Direction direction, CancellationToken cancellationToken = default);
}

public interface ILayoutProfileStore
{
    Task SaveAsync(LayoutProfile profile, CancellationToken cancellationToken = default);
    Task<LayoutProfile?> LoadAsync(string name, CancellationToken cancellationToken = default);
}
