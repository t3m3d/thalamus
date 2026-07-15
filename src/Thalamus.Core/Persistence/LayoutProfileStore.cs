using System.Security.Cryptography;
using System.Text;
using Thalamus.Core.Models;
using Thalamus.Core.Services;

namespace Thalamus.Core.Persistence;

public sealed class LayoutProfileStore(string profileDirectory) : ILayoutProfileStore
{
    private readonly string _profileDirectory = string.IsNullOrWhiteSpace(profileDirectory)
        ? throw new ArgumentException("A profile directory is required.", nameof(profileDirectory))
        : profileDirectory;


    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "CONIN$", "CONOUT$",
        "COM\u00B9", "COM\u00B2", "COM\u00B3",
        "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
    };
    public Task SaveAsync(LayoutProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateName(profile.Name);
        if (!IsValid(profile, profile.Name))
            throw new ArgumentException("The layout profile contains invalid placement data.", nameof(profile));
        return FileFor(profile.Name).SaveAsync(profile, cancellationToken);
    }

    public async Task<LayoutProfile?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        return await FileFor(name).LoadAsync(profile => IsValid(profile, name), cancellationToken)
            .ConfigureAwait(false);
    }

    public static string SafeFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ValidateName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        if (string.Equals(sanitized, name, StringComparison.Ordinal) && !name.StartsWith('!'))
            return sanitized;

        var canonicalName = name.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName)))[..32];
        return $"!{sanitized}-{hash}";
    }

    private AtomicJsonFile<LayoutProfile> FileFor(string name) =>
        new(Path.Combine(_profileDirectory, SafeFileName(name) + ".json"));


    private static bool IsValid(LayoutProfile profile, string expectedName)
    {
        if (profile.Version != LayoutProfile.CurrentVersion ||
            !string.Equals(profile.Name, expectedName, StringComparison.OrdinalIgnoreCase) ||
            profile.Monitors is null || profile.Windows is null ||
            profile.Monitors.Count is 0 or > 64 || profile.Windows.Count > 10_000)
            return false;

        var monitorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in profile.Monitors)
        {
            if (monitor is null || !IsSafeText(monitor.DeviceName, 256) ||
                !IsSafeRectangle(monitor.Bounds) || !IsSafeRectangle(monitor.WorkArea) ||
                !IsContained(monitor.WorkArea, monitor.Bounds) ||
                monitor.DpiX is 0 or > 960 || monitor.DpiY is 0 or > 960 ||
                !monitorNames.Add(monitor.DeviceName))
                return false;
        }

        if (profile.Monitors.Count(monitor => monitor.IsPrimary) > 1)
            return false;

        var placementKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return profile.Windows.All(window =>
            window is not null &&
            IsSafeText(window.ApplicationId, 512) && IsSafeText(window.ClassName, 512) &&
            window.Ordinal is >= 0 and < 10_000 &&
            IsSafeRectangle(window.Bounds) &&
            IsSafeText(window.MonitorDeviceName, 256) &&
            monitorNames.Contains(window.MonitorDeviceName) &&
            placementKeys.Add($"{window.ApplicationId}\u001f{window.ClassName}\u001f{window.Ordinal}"));
    }

    private static bool IsContained(RectI inner, RectI outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool IsSafeRectangle(RectI rectangle)
    {
        const long coordinateLimit = 1_000_000;
        return rectangle.IsValid &&
            rectangle.Width <= coordinateLimit && rectangle.Height <= coordinateLimit &&
            Math.Abs((long)rectangle.X) <= coordinateLimit &&
            Math.Abs((long)rectangle.Y) <= coordinateLimit &&
            Math.Abs((long)rectangle.Right) <= coordinateLimit &&
            Math.Abs((long)rectangle.Bottom) <= coordinateLimit;
    }

    private static bool IsSafeText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        var stem = trimmed.Split('.')[0].TrimEnd(' ', '.');
        return name.Length <= 80 && name == trimmed &&
            name is not "." and not ".." && !name.EndsWith('.') &&
            !name.Any(char.IsControl) && !ReservedNames.Contains(stem);
    }

    private static void ValidateName(string? name)
    {
        if (!IsValidName(name))
            throw new ArgumentException(
                "Layout names must contain 1-80 non-reserved characters without outer whitespace or a trailing period.",
                nameof(name));
    }
}
