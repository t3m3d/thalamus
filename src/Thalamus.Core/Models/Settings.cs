namespace Thalamus.Core.Models;

public sealed record ThalamusSettings
{
    public int Version { get; init; } = CurrentVersion;
    public const int CurrentVersion = 1;
    public string ThemePreset { get; init; } = "Cerebrum";
    public string AccentColor { get; init; } = "#7DD3FC";
    public double SurfaceOpacity { get; init; } = 0.86;
    public bool ReducedMotion { get; init; }
    public HotkeySettings OverviewHotkey { get; init; } = new();
}

public sealed record HotkeySettings
{
    public bool Control { get; init; } = true;
    public bool Alt { get; init; } = true;
    public bool Shift { get; init; }
    public bool Windows { get; init; }
    public int VirtualKey { get; init; } = 0x20;
}
