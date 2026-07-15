using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Thalamus.Core.Persistence;

internal static class AtomicJsonFileGateRegistry
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates =
        new(StringComparer.OrdinalIgnoreCase);

    internal static SemaphoreSlim ForPath(string path) =>
        FileGates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
}

public sealed class AtomicJsonFile<T>(
    string path,
    JsonSerializerOptions? options = null,
    long maximumFileLength = 32L * 1024 * 1024)
{
    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CrossProcessLockRetryDelay = TimeSpan.FromMilliseconds(40);
    private readonly long _maximumFileLength = maximumFileLength > 0
        ? maximumFileLength
        : throw new ArgumentOutOfRangeException(nameof(maximumFileLength));
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Path { get; } = path is null
        ? throw new ArgumentNullException(nameof(path))
        : string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A JSON file path is required.", nameof(path))
            : System.IO.Path.GetFullPath(path);
    public string BackupPath => Path + ".bak";

    public Task SaveAsync(T value, CancellationToken cancellationToken = default) =>
        WriteAsync(value, preserveBackup: false, cancellationToken);

    public Task RepairAsync(T value, CancellationToken cancellationToken = default) =>
        WriteAsync(value, preserveBackup: true, cancellationToken);

    private async Task WriteAsync(
        T value,
        bool preserveBackup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var gate = GateForPath();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock =
                await AcquireCrossProcessLockAsync(cancellationToken).ConfigureAwait(false);
            await SaveCoreAsync(value, preserveBackup, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveCoreAsync(
        T value,
        bool preserveBackup,
        CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("The JSON file must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{Path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using var boundedStream = new LengthLimitedWriteStream(stream, _maximumFileLength);
                await JsonSerializer.SerializeAsync(boundedStream, value, _options, cancellationToken)
                    .ConfigureAwait(false);
                await boundedStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > _maximumFileLength)
                    throw new InvalidDataException("The serialized JSON exceeds the configured file limit.");
                stream.Flush(true);
            }

            if (preserveBackup)
                File.Move(temporaryPath, Path, overwrite: true);
            else if (File.Exists(Path))
                File.Replace(temporaryPath, Path, BackupPath, true);
            else
                File.Move(temporaryPath, Path);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A stale uniquely named temporary file is harmless and may be removed later.
            }
        }
    }

    public Task<T?> LoadAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(static _ => true, cancellationToken);

    public async Task<T?> LoadAsync(
        Func<T, bool> validator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var gate = GateForPath();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock =
                await AcquireCrossProcessLockAsync(cancellationToken).ConfigureAwait(false);
            var current = await TryLoadAsync(Path, cancellationToken).ConfigureAwait(false);
            if (current is { Success: true, Value: not null } && validator(current.Value))
                return current.Value;

            var backup = await TryLoadAsync(BackupPath, cancellationToken).ConfigureAwait(false);
            if (backup is not { Success: true, Value: not null } || !validator(backup.Value))
                return default;

            await TryRepairAsync(backup.Value, cancellationToken).ConfigureAwait(false);

            return backup.Value;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task TryRepairAsync(T value, CancellationToken cancellationToken)
    {
        try
        {
            await SaveCoreAsync(value, preserveBackup: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // A valid backup remains usable when self-healing is not permitted.
        }
    }

    private SemaphoreSlim GateForPath() => AtomicJsonFileGateRegistry.ForPath(Path);

    private async Task<FileStream> AcquireCrossProcessLockAsync(
        CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("The JSON file must have a parent directory.");
        Directory.CreateDirectory(directory);
        var lockPath = Path + ".lock";
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (IOException exception)
            {
                if (timer.Elapsed >= CrossProcessLockTimeout)
                {
                    throw new IOException(
                        "Timed out waiting for exclusive access to the JSON file.",
                        exception);
                }

                await Task.Delay(CrossProcessLockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
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
            if (stream.Length is <= 0 || stream.Length > _maximumFileLength)
                return (false, default);

            var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken)
                .ConfigureAwait(false);
            return value is null ? (false, default) : (true, value);
        }
        catch (JsonException)
        {
            return (false, default);
        }
        catch (IOException)
        {
            return (false, default);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, default);
        }
    }

    private sealed class LengthLimitedWriteStream(Stream inner, long maximumLength) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            inner.Write(buffer);
            _written += buffer.Length;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _written += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacity(1);
            inner.WriteByte(value);
            _written++;
        }

        private void EnsureCapacity(int count)
        {
            if (_written > maximumLength - count)
                throw new InvalidDataException("The serialized JSON exceeds the configured file limit.");
        }
    }
}
