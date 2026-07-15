using Thalamus.Core.Commands;
using Thalamus.Core.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class VirtualDesktopFallbackTests
{
    [TestMethod]
    public async Task UnsupportedWorkspaceOperationsFailClearlyAndSafely()
    {
        const string explanation = "No documented adjacent-workspace API.";
        var service = new UnavailableVirtualDesktopService(explanation);

        Assert.IsFalse(service.Capabilities.CanSwitchAdjacent);
        Assert.AreEqual(explanation, service.Capabilities.Explanation);
        Assert.IsFalse(await service.SwitchAsync(Direction.Next));
        Assert.IsFalse(await service.MoveWindowAsync(42, Direction.Previous));
    }

    [TestMethod]
    public async Task CancellationIsHonoredBeforeFallback()
    {
        var service = new UnavailableVirtualDesktopService("Unavailable");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.SwitchAsync(Direction.Next, cancellation.Token));
    }

    [TestMethod]
    public async Task NativeCapabilityProbeNeverClaimsUndocumentedAdjacentControl()
    {
        using var service = new Thalamus.Services.VirtualDesktopService();

        Assert.IsFalse(service.Capabilities.CanSwitchAdjacent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(service.Capabilities.Explanation));
        Assert.IsFalse(await service.SwitchAsync(Direction.Next));
        Assert.IsFalse(await service.MoveWindowAsync(42, Direction.Previous));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.MoveWindowAsync(42, Direction.Next, cancellation.Token));
    }
}
