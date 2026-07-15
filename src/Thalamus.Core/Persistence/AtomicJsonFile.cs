using System.Text.Json;

namespace Thalamus.Core.Persistence;

public sealed class AtomicJsonFile<T>(string path, JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Path { get; } = path;
    public string BackupPath => Path + ".bak";

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("The JSON file must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path + ".tmp";

        await using (var stream = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(true);
        }

        if (File.Exists(Path))
            File.Replace(temporaryPath, Path, BackupPath, true);
        else
            File.Move(temporaryPath, Path);
    }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var current = await TryLoadAsync(Path, cancellationToken).ConfigureAwait(false);
        if (current.Success)
            return current.Value;

        var backup = await TryLoadAsync(BackupPath, cancellationToken).ConfigureAwait(false);
        return backup.Success ? backup.Value : default;
    }

    private async Task<(bool Success, T? Value)> TryLoadAsync(
        string candidatePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(candidatePath))
            return (false, default);

        try
        {
            await using var stream = new FileStream(
                candidatePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return (true, await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return (false, default);
        }
        catch (IOException)
        {
            return (false, default);
        }
    }
}
