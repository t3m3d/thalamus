using System.IO;

namespace Thalamus.Services;

internal static class AppPaths
{
    private const string DataRootEnvironmentVariable = "THALAMUS_DATA_ROOT";

    internal static string Root { get; } = ResolveRoot();

    internal static string SettingsFile => Path.Combine(Root, "settings.json");
    internal static string Profiles => Path.Combine(Root, "layouts");
    internal static string Diagnostics => Path.Combine(Root, "diagnostics.log");

    private static string ResolveRoot() => ResolveRoot(
        Environment.GetEnvironmentVariable(DataRootEnvironmentVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string ResolveRoot(string? isolatedRoot, string localApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(isolatedRoot))
        {
            if (!Path.IsPathFullyQualified(isolatedRoot))
                throw new InvalidOperationException(
                    $"{DataRootEnvironmentVariable} must be an absolute path.");

            return Path.GetFullPath(isolatedRoot);
        }

        if (string.IsNullOrWhiteSpace(localApplicationData) ||
            !Path.IsPathFullyQualified(localApplicationData))
            throw new InvalidOperationException(
                "The Windows local application-data directory is unavailable.");

        return Path.GetFullPath(Path.Combine(
            localApplicationData,
            "Cerebrum",
            "Thalamus"));
    }
}
