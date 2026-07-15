using Thalamus.Core.Models;
using Thalamus.Core.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class WindowRuleTests
{
    private static NativeWindowCandidate Eligible => new(
        42,
        true,
        true,
        false,
        false,
        false,
        false,
        "Chrome_WidgetWin_1",
        "Document",
        new RectI(-1200, 20, 800, 600),
        100);

    [TestMethod]
    public void AcceptsNormalTopLevelApplicationWindow() =>
        Assert.IsTrue(WindowRules.IsEligible(Eligible));

    [TestMethod]
    public void RejectsShellAndNonInteractiveWindows()
    {
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { ClassName = "Shell_TrayWnd" }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { IsToolWindow = true }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { IsCloaked = true }));
        Assert.IsTrue(WindowRules.IsEligible(Eligible with { IsOwned = true }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { HasNoActivateStyle = true }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { Title = "" }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { ClassName = "" }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with { Bounds = new RectI(0, 0, 20, 20) }));
        Assert.IsFalse(WindowRules.IsEligible(Eligible with
        { Bounds = new RectI(int.MaxValue, 0, 800, 600) }));
    }


    [TestMethod]
    public void RecognizesProtectedShellClassesWithoutCaseSensitivity()
    {
        Assert.IsTrue(WindowRules.IsProtectedClass("Progman"));
        Assert.IsTrue(WindowRules.IsProtectedClass("shell_secondarytraywnd"));
        Assert.IsFalse(WindowRules.IsProtectedClass("CabinetWClass"));
    }
    [TestMethod]
    public void GroupsApplicationsButKeepsEveryWindow()
    {
        var windows = new[]
        {
            Window(3, "editor", "B"),
            Window(1, "editor", "A"),
            Window(2, "browser", "C")
        };

        var groups = WindowGrouping.Group(windows);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("browser", groups[0].ApplicationId);
        Assert.AreEqual(2, groups[1].Windows.Count);
        Assert.AreEqual("A", groups[1].Windows[0].Title);
    }

    private static WindowSnapshot Window(long handle, string app, string title) =>
        new(handle, (int)handle, app, "Class", title, new RectI(0, 0, 500, 500), "DISPLAY1", false, false);
}
