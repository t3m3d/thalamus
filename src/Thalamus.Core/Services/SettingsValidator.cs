using System.Text.RegularExpressions;
using Thalamus.Core.Models;

namespace Thalamus.Core.Services;

public static partial class SettingsValidator
{
    private static readonly Dictionary<string, string> Presets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cerebrum"] = "Cerebrum",
            ["Krypton Glass"] = "Krypton Glass",
            ["Graphite"] = "Graphite",
            ["Frost"] = "Frost"
        };

    public static ThalamusSettings Normalize(ThalamusSettings? settings)
    {
        var defaults = new ThalamusSettings();
        if (settings is null || settings.Version != ThalamusSettings.CurrentVersion)
            return defaults;

        var preset = settings.ThemePreset is not null &&
            Presets.TryGetValue(settings.ThemePreset.Trim(), out var canonicalPreset)
                ? canonicalPreset
                : defaults.ThemePreset;
        var accent = settings.AccentColor is not null && AccentPattern().IsMatch(settings.AccentColor)
            ? settings.AccentColor.ToUpperInvariant()
            : defaults.AccentColor;
        var opacity = double.IsFinite(settings.SurfaceOpacity)
            ? Math.Clamp(settings.SurfaceOpacity, 0.45, 1)
            : defaults.SurfaceOpacity;

        var hotkey = settings.OverviewHotkey;
        if (hotkey is null ||
            hotkey.VirtualKey is <= 0 or > 0xFE ||
            !(hotkey.Control || hotkey.Alt || hotkey.Shift || hotkey.Windows))
            hotkey = defaults.OverviewHotkey;

        return settings with
        {
            ThemePreset = preset,
            AccentColor = accent,
            SurfaceOpacity = opacity,
            OverviewHotkey = hotkey
        };
    }

    public static bool ShouldPersistNormalization(
        ThalamusSettings? loaded,
        ThalamusSettings normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return loaded is null ||
            (loaded.Version == ThalamusSettings.CurrentVersion && loaded != normalized);
    }

    [GeneratedRegex(@"\A#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AccentPattern();
}
