using Thalamus.Core.Layout;
using Thalamus.Core.Models;

namespace Thalamus.Tests;

[TestClass]
public sealed class RestorePlannerTests
{
    [TestMethod]
    public void MatchesByApplicationClassAndOrdinal()
    {
        var savedMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040));
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "work",
            DateTimeOffset.UtcNow,
            [savedMonitor],
            [
                new WindowPlacementRecord("editor", "Main", 1, new RectI(100, 100, 800, 600), "DISPLAY1", true),
                new WindowPlacementRecord("missing", "Main", 0, new RectI(0, 0, 400, 400), "DISPLAY1", false)
            ]);
        var windows = new[]
        {
            Window(20, "editor", "Main"),
            Window(10, "editor", "Main"),
            Window(30, "editor", "Dialog")
        };
        var currentMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 2560, 1440),
            new RectI(0, 0, 2560, 1400));

        var plan = LayoutRestorePlanner.Plan(profile, windows, [currentMonitor]);

        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual(20L, plan[0].Handle);
        Assert.AreEqual(new RectI(133, 135, 800, 600), plan[0].Bounds);
        Assert.IsTrue(plan[0].MaximizeAfterPlacement);
    }

    [TestMethod]
    public void OrdinalsRemainStableWhenWindowHandlesChange()
    {
        var monitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            IsPrimary: true);
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "stable-order",
            DateTimeOffset.UtcNow,
            [monitor],
            [
                new WindowPlacementRecord(
                    "editor", "Main", 0, new RectI(10, 20, 700, 500), "DISPLAY1", false),
                new WindowPlacementRecord(
                    "editor", "Main", 1, new RectI(720, 20, 700, 500), "DISPLAY1", false)
            ]);
        var windows = new[]
        {
            Window(10, "editor", "Main") with { Title = "Zulu document" },
            Window(99, "editor", "Main") with { Title = "Alpha document" }
        };

        var plan = LayoutRestorePlanner.Plan(profile, windows, [monitor]);

        Assert.HasCount(2, plan);
        Assert.AreEqual(99L, plan[0].Handle);
        Assert.AreEqual(10L, plan[1].Handle);
    }

    [TestMethod]
    public void MissingSavedMonitorFallsBackToPrimaryAndStaysVisible()
    {
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "mobile",
            DateTimeOffset.UtcNow,
            [new MonitorSnapshot("OLD", new RectI(-1920, 0, 1920, 1080), new RectI(-1920, 0, 1920, 1040))],
            [new WindowPlacementRecord("terminal", "Console", 0, new RectI(-1900, 20, 900, 700), "OLD", false)]);
        var primary = new MonitorSnapshot(
            "NEW",
            new RectI(0, 0, 1366, 768),
            new RectI(0, 0, 1366, 728),
            IsPrimary: true);

        var plan = LayoutRestorePlanner.Plan(profile, [Window(5, "terminal", "Console")], [primary]);

        Assert.AreEqual(1, plan.Count);
        Assert.IsTrue(primary.WorkArea.Contains(plan[0].Bounds.X, plan[0].Bounds.Y));
        Assert.IsTrue(plan[0].Bounds.Right <= primary.WorkArea.Right);
        Assert.IsTrue(plan[0].Bounds.Bottom <= primary.WorkArea.Bottom);
    }

    [TestMethod]
    public void UnsupportedProfileVersionProducesNoActions()
    {
        var profile = new LayoutProfile(
            99,
            "future",
            DateTimeOffset.UtcNow,
            [],
            []);

        Assert.AreEqual(0, LayoutRestorePlanner.Plan(profile, [], []).Count);
    }

    [TestMethod]
    public void MatchesApplicationAndClassWithoutCaseSensitivity()
    {
        var monitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            IsPrimary: true);
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "mixed-case",
            DateTimeOffset.UtcNow,
            [monitor],
            [new WindowPlacementRecord("EDITOR", "MainWindow", 0, new RectI(20, 30, 800, 600), "display1", false)]);

        var plan = LayoutRestorePlanner.Plan(profile, [Window(42, "editor", "mainwindow")], [monitor]);

        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual(42L, plan[0].Handle);
    }

    [TestMethod]
    public void DuplicateProfileEntriesNeverPlanTheSameWindowTwice()
    {
        var monitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            IsPrimary: true);
        var placement = new WindowPlacementRecord(
            "editor", "Main", 0, new RectI(20, 30, 800, 600), "DISPLAY1", false);
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "duplicate",
            DateTimeOffset.UtcNow,
            [monitor],
            [placement, placement]);

        var plan = LayoutRestorePlanner.Plan(profile, [Window(42, "editor", "Main")], [monitor]);

        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual(42L, plan[0].Handle);
    }

    [TestMethod]
    public void ExtremeSavedGeometryFallsBackInsideCurrentWorkArea()
    {
        var savedMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1, 1),
            new RectI(0, 0, 1, 1));
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "extreme",
            DateTimeOffset.UtcNow,
            [savedMonitor],
            [new WindowPlacementRecord(
                "editor",
                "Main",
                0,
                new RectI(0, 0, 1_000_000, 1),
                "DISPLAY1",
                false)]);
        var currentMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 3840, 2160),
            new RectI(0, 0, 3840, 2120),
            IsPrimary: true);

        var plan = LayoutRestorePlanner.Plan(
            profile, [Window(42, "editor", "Main")], [currentMonitor]);

        Assert.HasCount(1, plan);
        Assert.AreEqual(new RectI(0, 0, 3840, 1), plan[0].Bounds);
    }

    [TestMethod]
    public void InvalidMonitorDpiDegradesWithoutThrowing()
    {
        var invalidSavedMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            0,
            0);
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "invalid-dpi",
            DateTimeOffset.UtcNow,
            [invalidSavedMonitor],
            [new WindowPlacementRecord(
                "editor", "Main", 0, new RectI(50, 60, 800, 600), "DISPLAY1", false)]);
        var currentMonitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            IsPrimary: true);

        var plan = LayoutRestorePlanner.Plan(
            profile, [Window(42, "editor", "Main")], [currentMonitor]);
        var noUsableTarget = LayoutRestorePlanner.Plan(
            profile, [Window(42, "editor", "Main")], [invalidSavedMonitor]);

        Assert.HasCount(1, plan);
        Assert.AreEqual(new RectI(50, 60, 800, 600), plan[0].Bounds);
        Assert.HasCount(0, noUsableTarget);
    }


    private static WindowSnapshot Window(long handle, string app, string className) =>
        new(handle, (int)handle, app, className, app, new RectI(0, 0, 400, 300), "DISPLAY1", false, false);
}
