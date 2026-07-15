using Thalamus.Core.Commands;
using Thalamus.Core.Layout;
using Thalamus.Core.Persistence;
using Thalamus.Core.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class ContractValidationTests
{
    [TestMethod]
    public void PublicContractsRejectNullInputs()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CommandParser.Parse(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => CommandParser.ToArguments(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => CommandEnvelope.Create(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => WindowRules.IsEligible(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => WindowGrouping.Group(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => MonitorGeometry.BestMonitor(default, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LayoutRestorePlanner.Plan(null!, [], []));
        Assert.ThrowsExactly<ArgumentException>(() => new LayoutProfileStore(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LayoutProfileStore.SafeFileName(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new AtomicJsonFile<object>(null!));
        Assert.ThrowsExactly<ArgumentException>(() => new AtomicJsonFile<object>(" "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AtomicJsonFile<object>("valid.json", maximumFileLength: 0));
    }

    [TestMethod]
    public async Task AtomicFileRejectsNullValues()
    {
        var file = new AtomicJsonFile<object>(Path.Combine(
            Path.GetTempPath(),
            "Thalamus.Tests",
            Guid.NewGuid().ToString("N"),
            "value.json"));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => file.SaveAsync(null!));
    }

    [TestMethod]
    public void ParserRejectsNullElementWithoutThrowing()
    {
        var result = CommandParser.Parse([null!, "left"]);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }
}
