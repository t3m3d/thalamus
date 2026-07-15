using Thalamus.Core.Models;
using Thalamus.Core.Services;

namespace Thalamus.Core.Persistence;

public sealed class LayoutProfileStore(string profileDirectory) : ILayoutProfileStore
{
    public Task SaveAsync(LayoutProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateName(profile.Name);
        return FileFor(profile.Name).SaveAsync(profile, cancellationToken);
    }

    public async Task<LayoutProfile?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var profile = await FileFor(name).LoadAsync(cancellationToken).ConfigureAwait(false);
        return profile?.Version == LayoutProfile.CurrentVersion ? profile : null;
    }

    public static string SafeFileName(string name)
    {
        ValidateName(name);
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Trim().Select(c => invalid.Contains(c) ? '_' : c));
    }

    private AtomicJsonFile<LayoutProfile> FileFor(string name) =>
        new(Path.Combine(profileDirectory, SafeFileName(name) + ".json"));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name is "." or "..")
            throw new ArgumentException("Layout names must contain 1-80 characters.", nameof(name));
    }
}
