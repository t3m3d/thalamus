using System.Windows;
using System.Windows.Media;
using Thalamus.Core.Models;

namespace Thalamus.Services;

internal static class ThemeService
{
    internal static void Apply(Window window, ThalamusSettings settings)
    {
        var palette = settings.ThemePreset.ToLowerInvariant() switch
        {
            "krypton glass" => new Palette("#09131F", "#17283A", "#203A52", "#60A7C4DB"),
            "graphite" => new Palette("#121315", "#24272B", "#30343A", "#4DFFFFFF"),
            "frost" => new Palette("#101923", "#243546", "#304A60", "#5AABD8F1"),
            _ => new Palette("#12151A", "#212730", "#2B3440", "#38FFFFFF")
        };

        var opacity = Math.Clamp(settings.SurfaceOpacity, 0.45, 1);
        window.Background = Brush(palette.Background, Math.Min(1, opacity + 0.08));
        window.Resources["SurfaceBrush"] = Brush(palette.Surface, opacity);
        window.Resources["SurfaceHoverBrush"] = Brush(palette.Hover, Math.Min(1, opacity + 0.08));
        window.Resources["BorderBrush"] = Brush(palette.Border, 1);
        window.Resources["AccentBrush"] = Brush(Parse(settings.AccentColor, "#7DD3FC"), 1);
        window.Resources["PreviewBackgroundBrush"] = Brush("#11151B", 1);
        window.Resources["PrimaryTextBrush"] = Brush("#F7FAFC", 1);
        window.Resources["SecondaryTextBrush"] = Brush("#D7DEE8", 0.82);
        window.Resources["MutedTextBrush"] = Brush("#D7DEE8", 0.58);
        window.Resources["ToolbarBackgroundBrush"] = Brush("#FFFFFF", 0.11);
        window.Resources["ToolbarHoverBrush"] = Brush("#FFFFFF", 0.19);
        window.Resources["ToolbarHoverTextBrush"] = Brush("#F7FAFC", 1);
        window.Resources["SelectionHoverBrush"] = Brush("#FFFFFF", 0.14);
        window.Resources["SelectionBackgroundBrush"] = Brush(Parse(settings.AccentColor, "#7DD3FC"), 0.13);
        window.Resources["IconBackgroundBrush"] = Brush("#FFFFFF", 0.14);
        window.Foreground = (Brush)window.Resources["PrimaryTextBrush"];
    }

    private static SolidColorBrush Brush(string color, double opacity) =>
        Brush(Parse(color, "#12151A"), opacity);

    private static SolidColorBrush Brush(Color color, double opacity) =>
        new(Color.FromArgb(
            (byte)Math.Round(color.A * opacity),
            color.R,
            color.G,
            color.B));

    private static Color Parse(string value, string fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(fallback);
        }
    }

    private sealed record Palette(string Background, string Surface, string Hover, string Border);
}
