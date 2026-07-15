using Thalamus.Core.Commands;
using Thalamus.Core.Models;
using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class WindowManagerContractTests
{
    [TestMethod]
    public async Task CloseReportsRejectedInvalidHandle()
    {
        var manager = new WindowManager();

        var accepted = await manager.CloseAsync(0);

        Assert.IsFalse(accepted);
        Assert.IsFalse(manager.Activate(0));
        Assert.IsFalse(manager.Minimize(0));
        Assert.IsFalse(manager.Tile(0, TileTarget.Left));
        Assert.IsFalse(manager.MoveToMonitor(0, "DISPLAY1"));
        Assert.IsFalse(manager.ApplyPlacement(0, new RectI(10, 10, 800, 600), false));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => manager.CloseAsync(0, cancellation.Token));

        var monitors = manager.GetMonitors();
        Assert.IsTrue(monitors.All(monitor =>
            monitor.Bounds.IsValid && monitor.WorkArea.IsValid && monitor.DpiX > 0 && monitor.DpiY > 0));
    }

    [TestMethod]
    public async Task NativeEnumerationIsSafeOnInteractiveOrDisconnectedDesktop()
    {
        var manager = new WindowManager();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            var windows = await manager.GetWindowsAsync(cancellation.Token);

            Assert.IsTrue(windows.All(window =>
                window.Handle != 0 &&
                window.ProcessId > 0 &&
                window.ProcessId != Environment.ProcessId &&
                !string.IsNullOrWhiteSpace(window.ApplicationId) &&
                !string.IsNullOrWhiteSpace(window.ClassName) &&
                !string.IsNullOrWhiteSpace(window.Title) &&
                window.Bounds.IsValid));
            Assert.AreEqual(
                windows.Count,
                windows.Select(window => window.Handle).Distinct().Count());
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual(0L, manager.GetForegroundWindow());
            StringAssert.Contains(exception.Message, "window enumeration failed");
        }
    }
}

