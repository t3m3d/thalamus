using System.IO;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Thalamus.Core.Commands;

namespace Thalamus.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexBaseName = @"Local\Cerebrum.Thalamus.Primary";
    private const string PipeBaseName = "Cerebrum.Thalamus.Commands.v1";
    private static readonly string CurrentUserScope = CreateCurrentUserScope();
    private static readonly int CurrentSessionId = GetCurrentSessionId();
    private static readonly string MutexName =
        $"{MutexBaseName}.user-{CurrentUserScope}.session-{CurrentSessionId}";
    private static readonly string CurrentPipeName = CreateSessionPipeName();
    internal static string SessionPipeName => CurrentPipeName;
    private readonly Mutex _mutex;
    private const int MaximumCommandLength = 4096;
    private static readonly TimeSpan DefaultRequestReadTimeout = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _pipeName;
    private readonly TimeSpan _requestReadTimeout;
    private Task? _serverTask;
    private Func<AppCommand, Task>? _handler;

    private int _disposed;
    internal SingleInstanceService(
        string? mutexName = null,
        string? pipeName = null,
        TimeSpan? requestReadTimeout = null)
    {
        _requestReadTimeout = requestReadTimeout ?? DefaultRequestReadTimeout;
        if (_requestReadTimeout <= TimeSpan.Zero ||
            _requestReadTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
            throw new ArgumentOutOfRangeException(
                nameof(requestReadTimeout),
                "The request read timeout must be positive and supported by timers.");

        _pipeName = pipeName ?? CurrentPipeName;
        _mutex = new Mutex(true, mutexName ?? MutexName, out var createdNew);
        var ownsMutex = createdNew;
        if (!ownsMutex)
        {
            try
            {
                ownsMutex = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
        }
        IsPrimary = ownsMutex;
    }

    private static string CreateSessionPipeName()
        => $"{PipeBaseName}.user-{CurrentUserScope}.session-{CurrentSessionId}";

    private static int GetCurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static string CreateCurrentUserScope()
    {
        string? identityName = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            identityName = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(identityName))
                identityName = identity.Name;
        }
        catch (Exception exception) when (
            exception is SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The account name fallback remains stable within this Windows profile.
        }

        if (string.IsNullOrWhiteSpace(identityName))
            identityName = $"{Environment.UserDomainName}\\{Environment.UserName}";

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identityName)))[..16];
    }

    internal bool IsPrimary { get; }
    internal Action<Exception>? ServerFaulted { get; set; }


    internal void Start(Func<AppCommand, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can host the command pipe.");
        if (_serverTask is not null)
            throw new InvalidOperationException("The command pipe is already running.");

        _handler = handler;
        _serverTask = RunServerAsync(_shutdown.Token);
    }

    internal static async Task<bool> ForwardAsync(
        AppCommand command,
        TimeSpan timeout,
        string? pipeName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await using var client = new NamedPipeClientStream(
                ".", pipeName ?? CurrentPipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync(CommandEnvelope.Create(command).Serialize()).ConfigureAwait(false);

            using var reader = new StreamReader(
                client, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var response = await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            return string.Equals(response, "ok", StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    using var reader = new StreamReader(
                        server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                    await using var writer = new StreamWriter(
                        server, new UTF8Encoding(false), leaveOpen: true)
                    {
                        AutoFlush = true
                    };
                    using var requestDeadline =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestDeadline.CancelAfter(_requestReadTimeout);
                    var line = await ReadLineLimitedAsync(reader, MaximumCommandLength, requestDeadline.Token).ConfigureAwait(false);
                    var envelope = line is null ? null : CommandEnvelope.Deserialize(line);
                    if (envelope is null || _handler is null)
                    {
                        await writer.WriteLineAsync("error").ConfigureAwait(false);
                        continue;
                    }

                    if (envelope.Command.Kind == AppCommandKind.Exit)
                    {
                        await writer.WriteLineAsync("ok").ConfigureAwait(false);
                        try
                        {
                            await _handler(envelope.Command).ConfigureAwait(false);
                        }
                        catch
                        {
                            // The caller already has its shutdown acknowledgment.
                        }
                        continue;
                    }

                    try
                    {
                        await _handler(envelope.Command).ConfigureAwait(false);
                        await writer.WriteLineAsync("ok").ConfigureAwait(false);
                    }
                    catch
                    {
                        await writer.WriteLineAsync("error").ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // An idle client cannot monopolize the single command listener.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal bounded shutdown, including cancellation during an I/O retry delay.
        }
        catch (Exception exception)
        {
            try
            {
                ServerFaulted?.Invoke(exception);
            }
            catch
            {
                // Diagnostics must never fault the listener task.
            }
        }
    }

    private static async Task<string?> ReadLineLimitedAsync(
        StreamReader reader,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        var value = new StringBuilder(maximumLength);
        var buffer = new char[256];
        while (value.Length <= maximumLength)
        {
            var remaining = maximumLength + 1 - value.Length;
            var read = await reader.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return value.Length == 0 ? null : value.ToString();

            for (var index = 0; index < read; index++)
            {
                if (buffer[index] is '\r' or '\n')
                    return value.ToString();

                value.Append(buffer[index]);
                if (value.Length > maximumLength)
                    return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _shutdown.Cancel();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown must remain bounded.
        }

        _shutdown.Dispose();
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The owning thread has already ended; process teardown releases the handle.
            }
        }
        _mutex.Dispose();
    }
}
