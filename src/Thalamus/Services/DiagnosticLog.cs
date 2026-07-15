using System.IO;

namespace Thalamus.Services;

internal sealed class DiagnosticLog
{
    private const long DefaultMaximumBytes = 1024 * 1024;
    private readonly string _path;
    private readonly long _maximumBytes;

    internal DiagnosticLog(string? path = null, long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        _path = Path.GetFullPath(path ?? AppPaths.Diagnostics);
        _maximumBytes = maximumBytes;
    }

    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;

    internal void Write(string eventCode, string? safeDetail = null)
    {
        var code = Sanitize(eventCode, 64) ?? "THA-UNKNOWN";
        var detail = Sanitize(safeDetail, 256);
        var line = $"{DateTimeOffset.UtcNow:O}\t{code}";
        if (detail is not null)
            line += $"\t{detail}";
        line += Environment.NewLine;

        lock (_sync)
        {
            _tail = _tail.ContinueWith(
                _ => AppendAsync(line),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    internal bool Flush(TimeSpan timeout)
    {
        Task pending;
        lock (_sync)
            pending = _tail;

        try
        {
            return pending.Wait(timeout);
        }
        catch
        {
            return false;
        }
    }

    private async Task AppendAsync(string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The diagnostics file must have a parent directory.");
            Directory.CreateDirectory(directory);
            if (File.Exists(_path) && new FileInfo(_path).Length >= _maximumBytes)
                File.Move(_path, _path + ".1", overwrite: true);
            await File.AppendAllTextAsync(_path, line).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never affect window management.
        }
    }

    private static string? Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var sanitized = new string(value
            .Take(maximumLength)
            .Select(character =>
                char.IsControl(character) || character is '\u2028' or '\u2029'
                    ? ' ' : character)
            .ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }
}
