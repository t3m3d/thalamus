using Thalamus.Core.Commands;
using Thalamus.Core.Layout;
using Thalamus.Core.Models;

namespace Thalamus.Tests;

[TestClass]
public sealed class LayoutGeometryTests
{
    private static readonly RectI WorkArea = new(-1920, 0, 1919, 1039);

    [TestMethod]
    public void HalvesCoverOddSizedWorkAreaWithoutGap()
    {
        var left = SnapLayout.Calculate(WorkArea, TileTarget.Left);
        var right = SnapLayout.Calculate(WorkArea, TileTarget.Right);

        Assert.AreEqual(WorkArea.X, left.X);
        Assert.AreEqual(left.Right, right.X);
        Assert.AreEqual(WorkArea.Right, right.Right);
        Assert.AreEqual(WorkArea.Width, left.Width + right.Width);
    }

    [TestMethod]
    public void ThirdsCoverOddSizedWorkAreaWithoutGap()
    {
        var left = SnapLayout.Calculate(WorkArea, TileTarget.LeftThird);
        var center = SnapLayout.Calculate(WorkArea, TileTarget.CenterThird);
        var right = SnapLayout.Calculate(WorkArea, TileTarget.RightThird);

        Assert.AreEqual(left.Right, center.X);
        Assert.AreEqual(center.Right, right.X);
        Assert.AreEqual(WorkArea.Right, right.Right);
        Assert.AreEqual(WorkArea.Width, left.Width + center.Width + right.Width);
    }

    [TestMethod]
    public void QuartersRespectNegativeMonitorCoordinates()
    {
        var bottomRight = SnapLayout.Calculate(WorkArea, TileTarget.BottomRight);

        Assert.IsTrue(bottomRight.X < 0);
        Assert.AreEqual(WorkArea.Right, bottomRight.Right);
        Assert.AreEqual(WorkArea.Bottom, bottomRight.Bottom);
    }

    [TestMethod]
    public void EverySnapTargetRemainsValidForSmallestWorkArea()
    {
        var tiny = new RectI(int.MaxValue - 1, int.MinValue, 1, 1);

        foreach (var target in Enum.GetValues<TileTarget>())
        {
            var result = SnapLayout.Calculate(tiny, target);

            Assert.IsTrue(result.IsValid, target.ToString());
            Assert.IsTrue(tiny.Contains(result.X, result.Y), target.ToString());
            Assert.IsTrue(result.Right <= tiny.Right, target.ToString());
            Assert.IsTrue(result.Bottom <= tiny.Bottom, target.ToString());
        }
    }


    [TestMethod]
    public void RectangleValidityRejectsCoordinateOverflow()
    {
        var horizontalOverflow = new RectI(int.MaxValue, 0, 1, 1);
        var verticalOverflow = new RectI(0, int.MaxValue, 1, 1);
        var valid = new RectI(0, 0, 100, 100);

        Assert.IsFalse(horizontalOverflow.IsValid);
        Assert.IsFalse(verticalOverflow.IsValid);
        Assert.IsFalse(horizontalOverflow.Contains(int.MaxValue, 0));
        Assert.AreEqual(default, horizontalOverflow.Intersect(valid));
    }

    [TestMethod]
    public void RejectsInvalidWorkAreaBeforeClamping()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MonitorGeometry.FitToWorkArea(new RectI(10, 10, 100, 100), default));
    }
    [TestMethod]
    public void RejectsInvalidDpiGeometryAndOverflow()
    {
        var workArea = new RectI(0, 0, 1920, 1040);
        Assert.ThrowsExactly<ArgumentException>(() =>
            MonitorGeometry.FitToWorkArea(default, workArea));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MonitorGeometry.MapBetweenMonitors(default, workArea, workArea));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DpiPlacement.Scale(default, 96, 144));

        var targetDpi = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DpiPlacement.Scale(new RectI(0, 0, 100, 100), 96, 0));
        Assert.AreEqual("targetDpi", targetDpi.ParamName);
        Assert.ThrowsExactly<OverflowException>(() =>
            DpiPlacement.Scale(new RectI(0, 0, int.MaxValue, 1), 1, uint.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() =>
            DpiPlacement.Scale(new RectI(1_000_000_000, 0, 1_000_000_000, 1), 2, 3));
        Assert.ThrowsExactly<OverflowException>(() =>
            MonitorGeometry.MapBetweenMonitors(
                new RectI(int.MinValue, 0, 1, 1),
                new RectI(int.MaxValue - 1, 0, 1, 1),
                new RectI(0, 0, int.MaxValue, 1)));
    }

    [TestMethod]
    public void DpiScalingUsesAwayFromZeroRounding()
    {
        var scaled = DpiPlacement.Scale(new RectI(-101, 25, 801, 601), 96, 144);

        Assert.AreEqual(new RectI(-152, 38, 1202, 902), scaled);
    }

    [TestMethod]
    public void FindsMonitorWithLargestIntersection()
    {
        var monitors = new[]
        {
            new MonitorSnapshot("LEFT", new RectI(-1920, 0, 1920, 1080), new RectI(-1920, 0, 1920, 1040)),
            new MonitorSnapshot("MAIN", new RectI(0, 0, 2560, 1440), new RectI(0, 0, 2560, 1400), IsPrimary: true)
        };

        var selected = MonitorGeometry.BestMonitor(new RectI(-200, 100, 800, 700), monitors);

        Assert.AreEqual("MAIN", selected!.DeviceName);
    }

    [TestMethod]
    public void MappingChangedMonitorClampsInsideWorkArea()
    {
        var source = new RectI(-1920, 0, 1920, 1040);
        var target = new RectI(0, 0, 1280, 720);

        var mapped = MonitorGeometry.MapBetweenMonitors(new RectI(-2000, 900, 1200, 900), source, target);

        Assert.IsTrue(target.Contains(mapped.X, mapped.Y));
        Assert.IsTrue(mapped.Right <= target.Right);
        Assert.IsTrue(mapped.Bottom <= target.Bottom);
    }

    [TestMethod]
    public void MonitorMappingPreservesLogicalSizeAcrossDifferentDpi()
    {
        var source = new MonitorSnapshot(
            "LOW-DPI",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            96,
            96);
        var target = new MonitorSnapshot(
            "HIGH-DPI",
            new RectI(1920, 0, 1920, 1080),
            new RectI(1920, 0, 1920, 1040),
            192,
            192);

        var mapped = MonitorGeometry.MapBetweenMonitors(
            new RectI(100, 100, 400, 300), source, target);

        Assert.AreEqual(new RectI(2020, 100, 800, 600), mapped);
    }

    [TestMethod]
    public void MonitorMappingScalesWidthAndHeightByTheirOwnDpiAxes()
    {
        var source = new MonitorSnapshot(
            "SOURCE",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            96,
            96);
        var target = new MonitorSnapshot(
            "TARGET",
            new RectI(1920, 0, 1920, 1080),
            new RectI(1920, 0, 1920, 1040),
            192,
            144);

        var mapped = MonitorGeometry.MapBetweenMonitors(
            new RectI(100, 100, 400, 300), source, target);

        Assert.AreEqual(new RectI(2020, 100, 800, 450), mapped);
    }

    [TestMethod]
    public void DpiAwareMonitorMappingRejectsInvalidOrOverflowingInput()
    {
        var source = new MonitorSnapshot(
            "SOURCE",
            new RectI(0, 0, int.MaxValue, 1),
            new RectI(0, 0, int.MaxValue, 1),
            96,
            96);
        var target = new MonitorSnapshot(
            "TARGET",
            new RectI(0, 0, int.MaxValue, 1),
            new RectI(0, 0, int.MaxValue, 1),
            960,
            960);

        Assert.ThrowsExactly<OverflowException>(() =>
            MonitorGeometry.MapBetweenMonitors(
                new RectI(0, 0, int.MaxValue, 1), source, target));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MonitorGeometry.MapBetweenMonitors(
                new RectI(0, 0, 10, 1), source with { DpiX = 0 }, target));
    }

    [TestMethod]
    public void MappingPromotesDimensionsBeforeMultiplication()
    {
        var large = new RectI(0, 0, 100_000, 100_000);

        var mapped = MonitorGeometry.MapBetweenMonitors(large, large, large);

        Assert.AreEqual(large, mapped);
    }
    [TestMethod]
    public void WorkspaceCoordinatesAccountForTopAndLeftAppBars()
    {
        var monitorBounds = new RectI(0, 0, 1920, 1080);
        var workArea = new RectI(40, 32, 1880, 1008);
        var workspacePlacement = new RectI(100, 80, 800, 600);

        var screenPlacement = MonitorGeometry.WorkspaceToScreen(
            workspacePlacement,
            monitorBounds,
            workArea);

        Assert.AreEqual(new RectI(140, 112, 800, 600), screenPlacement);
        Assert.ThrowsExactly<OverflowException>(() =>
            MonitorGeometry.WorkspaceToScreen(
                new RectI(int.MaxValue - 10, 0, 10, 10),
                monitorBounds,
                workArea));
    }

    [TestMethod]
    public void WorkspaceCoordinatesUseMonitorLocalAppBarInsets()
    {
        var workspacePlacement = new RectI(2100, 80, 800, 600);
        var rightMonitor = new RectI(1920, 0, 1920, 1080);
        var rightWorkArea = new RectI(1920, 32, 1920, 1048);

        Assert.AreEqual(
            new RectI(2100, 112, 800, 600),
            MonitorGeometry.WorkspaceToScreen(
                workspacePlacement,
                rightMonitor,
                rightWorkArea));

        var leftWorkspacePlacement = new RectI(-1800, 80, 800, 600);
        var leftMonitor = new RectI(-1920, 0, 1920, 1080);
        var leftWorkArea = new RectI(-1880, 0, 1880, 1080);
        Assert.AreEqual(
            new RectI(-1760, 80, 800, 600),
            MonitorGeometry.WorkspaceToScreen(
                leftWorkspacePlacement,
                leftMonitor,
                leftWorkArea));
    }



    [TestMethod]
    public void RestoreKeepsReachablePlacementButRefitsDisconnectedMonitor()
    {
        var primary = new MonitorSnapshot(
            "MAIN",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            IsPrimary: true);
        var reachable = new RectI(-20, 50, 800, 600);
        var orphaned = new RectI(-1800, 100, 900, 700);
        var unreachableTitle = new RectI(100, -580, 800, 600);

        Assert.AreEqual(reachable, MonitorGeometry.EnsureVisible(reachable, [primary]));
        var titleRefitted = MonitorGeometry.EnsureVisible(unreachableTitle, [primary]);
        Assert.AreEqual(primary.WorkArea.Y, titleRefitted.Y);


        var refitted = MonitorGeometry.EnsureVisible(orphaned, [primary]);
        Assert.IsTrue(primary.WorkArea.Contains(refitted.X, refitted.Y));
        Assert.IsTrue(refitted.Right <= primary.WorkArea.Right);
        Assert.IsTrue(refitted.Bottom <= primary.WorkArea.Bottom);
    }

    [TestMethod]
    public void AdjacentMonitorUsesPhysicalOrderAndWraparound()
    {
        var monitors = new[]
        {
            new MonitorSnapshot("RIGHT", new RectI(2560, -200, 1920, 1080), new RectI(2560, -200, 1920, 1040)),
            new MonitorSnapshot("LEFT", new RectI(-1920, 0, 1920, 1080), new RectI(-1920, 0, 1920, 1040)),
            new MonitorSnapshot("MAIN", new RectI(0, 0, 2560, 1440), new RectI(0, 0, 2560, 1400), IsPrimary: true)
        };

        Assert.AreEqual("MAIN", MonitorGeometry.AdjacentMonitor(monitors, "left", 1)!.DeviceName);
        Assert.AreEqual("LEFT", MonitorGeometry.AdjacentMonitor(monitors, "MAIN", -1)!.DeviceName);
        Assert.AreEqual("LEFT", MonitorGeometry.AdjacentMonitor(monitors, "RIGHT", 1)!.DeviceName);
        Assert.AreEqual("RIGHT", MonitorGeometry.AdjacentMonitor(monitors, "LEFT", -1)!.DeviceName);
        Assert.AreEqual("RIGHT", MonitorGeometry.AdjacentMonitor(monitors, "MAIN", int.MaxValue)!.DeviceName);
    }

    [TestMethod]
    public void AdjacentMonitorFallsBackSafelyForChangedTopology()
    {
        var monitors = new[]
        {
            new MonitorSnapshot("INVALID", default, default),
            new MonitorSnapshot("SECONDARY", new RectI(-1600, 0, 1600, 900), new RectI(-1600, 0, 1600, 860)),
            new MonitorSnapshot("PRIMARY", new RectI(0, 0, 1920, 1080), new RectI(0, 0, 1920, 1040), IsPrimary: true)
        };

        Assert.AreEqual("PRIMARY", MonitorGeometry.AdjacentMonitor(monitors, "DISCONNECTED", 1)!.DeviceName);
        Assert.IsNull(MonitorGeometry.AdjacentMonitor([monitors[2]], "PRIMARY", 1));
    }



    [TestMethod]
    public void BestMonitorIgnoresInvalidSnapshots()
    {
        var invalidPrimary = new MonitorSnapshot("INVALID", default, default, IsPrimary: true);
        var valid = new MonitorSnapshot("VALID",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040));
        var window = new RectI(20, 30, 800, 600);

        var selected = MonitorGeometry.BestMonitor(window, [invalidPrimary, valid]);

        Assert.AreEqual("VALID", selected!.DeviceName);
        Assert.IsNull(MonitorGeometry.BestMonitor(window, [invalidPrimary]));
    }

    [TestMethod]
    public void DpiAwareMonitorMappingRejectsInvalidVerticalDpi()
    {
        var source = new MonitorSnapshot("SOURCE",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            96,
            0);
        var target = new MonitorSnapshot("TARGET",
            new RectI(1920, 0, 1920, 1080),
            new RectI(1920, 0, 1920, 1040),
            192,
            192);

        Assert.ThrowsExactly<ArgumentException>(() =>
            MonitorGeometry.MapBetweenMonitors(
                new RectI(20, 30, 800, 600), source, target));
    }

}
