using Thalamus.Core.Models;
using Thalamus.Core.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void NormalizesPresetAccentAndOpacity()
    {
        var input = new ThalamusSettings
        {
            ThemePreset = " graphite ",
            AccentColor = "#a1b2c3",
            SurfaceOpacity = 2
        };

        var result = SettingsValidator.Normalize(input);

        Assert.AreEqual("Graphite", result.ThemePreset);
        Assert.AreEqual("#A1B2C3", result.AccentColor);
        Assert.AreEqual(1, result.SurfaceOpacity);
        Assert.AreEqual(
            "#7DD3FC",
            SettingsValidator.Normalize(input with { AccentColor = "#A1B2C3\n" }).AccentColor);
    }

    [TestMethod]
    public void UnsafeHotkeyFallsBackToModifiedDefault()
    {
        var input = new ThalamusSettings
        {
            OverviewHotkey = new HotkeySettings
            {
                VirtualKey = 0x20,
                Control = false,
                Alt = false,
                Shift = false,
                Windows = false
            }
        };

        var result = SettingsValidator.Normalize(input);

        Assert.AreEqual(0x20, result.OverviewHotkey.VirtualKey);
        Assert.IsTrue(result.OverviewHotkey.Control);
        Assert.IsTrue(result.OverviewHotkey.Alt);
    }

    [TestMethod]
    public void UnknownContractVersionReturnsCurrentDefaults()
    {
        var result = SettingsValidator.Normalize(new ThalamusSettings
        {
            Version = 99,
            ThemePreset = "Frost",
            AccentColor = "#000000"
        });

        Assert.AreEqual(ThalamusSettings.CurrentVersion, result.Version);
        Assert.AreEqual("Cerebrum", result.ThemePreset);
        Assert.AreEqual("#7DD3FC", result.AccentColor);
    }

    [TestMethod]
    public void NormalizationDoesNotOverwriteUnknownContractVersion()
    {
        var future = new ThalamusSettings { Version = 99, ThemePreset = "Frost" };
        var normalized = SettingsValidator.Normalize(future);

        Assert.IsFalse(SettingsValidator.ShouldPersistNormalization(future, normalized));
        Assert.IsTrue(SettingsValidator.ShouldPersistNormalization(null, normalized));
        Assert.IsFalse(SettingsValidator.ShouldPersistNormalization(normalized, normalized));
        Assert.IsTrue(SettingsValidator.ShouldPersistNormalization(
            normalized with { SurfaceOpacity = 99 },
            normalized));
    }
}
