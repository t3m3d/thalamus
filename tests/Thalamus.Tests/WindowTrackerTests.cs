using Thalamus.Interop;
using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class WindowTrackerTests
{
    [TestMethod]
    public void SubscriberFailureIsContainedAndDisposeIsIdempotent()
    {
        using var invoked = new ManualResetEventSlim();
        var tracker = new WindowTracker();
        tracker.WindowsChanged += (_, _) =>
        {
            invoked.Set();
            throw new InvalidOperationException("Expected test callback failure.");
        };

        tracker.RequestRefresh();

        Assert.IsTrue(invoked.Wait(TimeSpan.FromSeconds(2)));
        tracker.Dispose();
        tracker.Dispose();
    }

    [TestMethod]
    public void RefreshClassificationIncludesDesktopAndCloakTransitions()
    {
        Assert.IsTrue(WindowTracker.IsRefreshEvent(
            NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH, IntPtr.Zero, NativeMethods.OBJID_WINDOW));
        Assert.IsTrue(WindowTracker.IsRefreshEvent(
            NativeMethods.EVENT_OBJECT_CLOAKED, new IntPtr(1), -4));
        Assert.IsTrue(WindowTracker.IsRefreshEvent(
            NativeMethods.EVENT_OBJECT_NAMECHANGE, new IntPtr(1), NativeMethods.OBJID_WINDOW));
        Assert.IsFalse(WindowTracker.IsRefreshEvent(
            NativeMethods.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, NativeMethods.OBJID_WINDOW));
        Assert.IsFalse(WindowTracker.IsRefreshEvent(
            0xFFFF, new IntPtr(1), NativeMethods.OBJID_WINDOW));
    }
}

