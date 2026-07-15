using System.Windows;
using System.Windows.Media;
using Thalamus.Core.Models;
using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class ThemeServiceTests
{
    [STATestMethod]
    public void ApplySupportsEveryDocumentedPalette()
    {
        var palettes = new Dictionary<string, Color>
        {
            ["Cerebrum"] = Color.FromArgb(240, 0x12, 0x15, 0x1A),
            ["Krypton Glass"] = Color.FromArgb(240, 0x09, 0x13, 0x1F),
            ["Graphite"] = Color.FromArgb(240, 0x12, 0x13, 0x15),
            ["Frost"] = Color.FromArgb(240, 0x10, 0x19, 0x23),
            ["unknown"] = Color.FromArgb(240, 0x12, 0x15, 0x1A)
        };

        foreach (var (preset, expected) in palettes)
        {
            var window = new Window();

            ThemeService.Apply(window, new ThalamusSettings { ThemePreset = preset });

            Assert.AreEqual(expected, Brush(window.Background).Color, preset);
            Assert.AreEqual(
                Brush(window.Resources["PrimaryTextBrush"]).Color,
                Brush(window.Foreground).Color,
                preset);
        }
    }

    [STATestMethod]
    public void ApplyFallsBackFromInvalidAccentAndClampsOpacity()
    {
        var minimumWindow = new Window();
        ThemeService.Apply(minimumWindow, new ThalamusSettings
        {
            AccentColor = "not-a-color",
            SurfaceOpacity = double.NegativeInfinity
        });

        var accent = Brush(minimumWindow.Resources["AccentBrush"]).Color;
        var surface = Brush(minimumWindow.Resources["SurfaceBrush"]).Color;
        Assert.AreEqual(Color.FromRgb(0x7D, 0xD3, 0xFC), accent);
        Assert.AreEqual((byte)115, surface.A);

        var maximumWindow = new Window();
        ThemeService.Apply(maximumWindow, new ThalamusSettings
        {
            SurfaceOpacity = double.PositiveInfinity
        });

        Assert.AreEqual(
            byte.MaxValue,
            Brush(maximumWindow.Resources["SurfaceBrush"]).Color.A);
    }

    private static SolidColorBrush Brush(object? value) =>
        value as SolidColorBrush ?? throw new AssertFailedException("Expected a solid color brush.");
}
