using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void UsesAbsoluteIsolatedRootWithoutRebasing()
    {
        var isolated = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "Thalamus.Tests", Guid.NewGuid().ToString("N")));

        Assert.AreEqual(isolated, AppPaths.ResolveRoot(isolated, "ignored"));
    }

    [TestMethod]
    public void RejectsRelativeIsolatedRoot()
    {
        var local = Path.GetFullPath(Path.GetTempPath());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AppPaths.ResolveRoot(Path.Combine("relative", "root"), local));
    }

    [TestMethod]
    public void DefaultsUnderAbsoluteLocalApplicationData()
    {
        var local = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "Thalamus.Tests", Guid.NewGuid().ToString("N")));

        var resolved = AppPaths.ResolveRoot(null, local);

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(local, "Cerebrum", "Thalamus")),
            resolved);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AppPaths.ResolveRoot(null, "relative-local-data"));
    }
}
