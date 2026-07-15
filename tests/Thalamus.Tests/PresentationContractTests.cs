using Thalamus.Core.Models;
using Thalamus.ViewModels;

namespace Thalamus.Tests;

[TestClass]
public sealed class PresentationContractTests
{
    [TestMethod]
    public void RelayCommandHonorsPredicateExecutesAndRaisesChange()
    {
        object? executed = null;
        var changed = 0;
        var command = new RelayCommand(
            value => executed = value,
            value => value is int number && number > 0);
        command.CanExecuteChanged += (_, _) => changed++;

        Assert.IsFalse(command.CanExecute(0));
        Assert.IsTrue(command.CanExecute(7));

        command.Execute("payload");
        command.RaiseCanExecuteChanged();

        Assert.AreEqual("payload", executed);
        Assert.AreEqual(1, changed);
    }

    [TestMethod]
    public void WindowCardExposesEveryBindingValueFromSnapshot()
    {
        var snapshot = new WindowSnapshot(
            42,
            7,
            "application",
            "MainWindow",
            "Document",
            new RectI(-900, 50, 800, 600),
            "DISPLAY2",
            true,
            false);

        var card = new WindowCardViewModel(snapshot, "application \u00B7 2 windows", null);

        Assert.AreSame(snapshot, card.Window);
        Assert.AreEqual(snapshot.Handle, card.Handle);
        Assert.AreEqual(snapshot.Title, card.Title);
        Assert.AreEqual(snapshot.ApplicationId, card.ApplicationId);
        Assert.AreEqual("application \u00B7 2 windows", card.GroupLabel);
        Assert.AreEqual(snapshot.MonitorDeviceName, card.MonitorDeviceName);
        Assert.AreEqual(snapshot.IsMinimized, card.IsMinimized);
        Assert.IsNull(card.Icon);
    }
}
