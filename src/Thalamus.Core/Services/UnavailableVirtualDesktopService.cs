using Thalamus.Core.Commands;

namespace Thalamus.Core.Services;

public sealed class UnavailableVirtualDesktopService(string explanation) : IVirtualDesktopService
{
    public VirtualDesktopCapabilities Capabilities { get; } =
        new(false, false, false, explanation);

    public Task<bool> SwitchAsync(Direction direction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<bool> MoveWindowAsync(
        long handle,
        Direction direction,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
