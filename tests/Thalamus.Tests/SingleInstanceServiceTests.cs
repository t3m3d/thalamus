using System.IO.Pipes;
using System.Text;
using Thalamus.Core.Commands;
using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class SingleInstanceServiceTests
{
    [TestMethod]
    public void SecondaryForwardsCommandAndReceivesHandlerAcknowledgment()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\Cerebrum.Thalamus.Tests.{suffix}";
        var pipeName = $"Cerebrum.Thalamus.Tests.{suffix}";
        using var primary = new SingleInstanceService(mutexName, pipeName);
        Assert.IsTrue(primary.IsPrimary);

        AppCommand? received = null;
        primary.Start(command =>
        {
            received = command;
            return Task.CompletedTask;
        });

        bool? secondaryIsPrimary = null;
        var secondaryThread = new Thread(() =>
        {
            using var secondary = new SingleInstanceService(mutexName, pipeName);
            secondaryIsPrimary = secondary.IsPrimary;
        });
        secondaryThread.Start();
        Assert.IsTrue(secondaryThread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(secondaryIsPrimary);
        var expected = new AppCommand(AppCommandKind.TileActive, "left-third");

        var forwarded = SingleInstanceService.ForwardAsync(
            expected, TimeSpan.FromSeconds(2), pipeName: pipeName).GetAwaiter().GetResult();

        Assert.IsTrue(forwarded);
        Assert.AreEqual(expected, received);
    }

    [TestMethod]
    public void HandlerFailureReturnsErrorAndServerAcceptsNextCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\Cerebrum.Thalamus.Tests.{suffix}";
        var pipeName = $"Cerebrum.Thalamus.Tests.{suffix}";
        using var primary = new SingleInstanceService(mutexName, pipeName);
        var attempts = 0;
        primary.Start(_ =>
            Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new InvalidOperationException("Expected test failure."))
                : Task.CompletedTask);

        var first = SingleInstanceService.ForwardAsync(
            AppCommand.Overview, TimeSpan.FromSeconds(2), pipeName: pipeName).GetAwaiter().GetResult();
        var second = SingleInstanceService.ForwardAsync(
            AppCommand.Overview, TimeSpan.FromSeconds(2), pipeName: pipeName).GetAwaiter().GetResult();

        Assert.IsFalse(first);
        Assert.IsTrue(second);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var service = new SingleInstanceService(
            $@"Local\Cerebrum.Thalamus.Tests.{suffix}",
            $"Cerebrum.Thalamus.Tests.{suffix}");

        service.Dispose();
        service.Dispose();
    }

    [TestMethod]
    public void OversizedPayloadIsRejectedAndServerRemainsAvailable()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\Cerebrum.Thalamus.Tests.{suffix}";
        var pipeName = $"Cerebrum.Thalamus.Tests.{suffix}";
        using var primary = new SingleInstanceService(mutexName, pipeName);
        primary.Start(_ => Task.CompletedTask);

        string? response;
        using (var client = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            client.Connect(2000);
            using var writer = new StreamWriter(
                client, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(
                client, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            writer.WriteLine(new string('x', 5000));
            response = reader.ReadLine();
        }

        Assert.AreEqual("error", response);
        var recovered = SingleInstanceService.ForwardAsync(
            AppCommand.Overview, TimeSpan.FromSeconds(2), pipeName: pipeName).GetAwaiter().GetResult();
        Assert.IsTrue(recovered);
    }

    [TestMethod]
    public async Task ForwardTimeoutCoversAcknowledgmentAndListenerRecovers()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"Cerebrum.Thalamus.Tests.{suffix}";
        using var primary = new SingleInstanceService(
            $@"Local\Cerebrum.Thalamus.Tests.{suffix}",
            pipeName);
        var attempts = 0;
        primary.Start(async _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                await Task.Delay(250);
        });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var timedOut = await SingleInstanceService.ForwardAsync(
            AppCommand.Overview,
            TimeSpan.FromMilliseconds(50),
            pipeName: pipeName);
        started.Stop();

        Assert.IsFalse(timedOut);
        Assert.IsLessThan(TimeSpan.FromSeconds(1), started.Elapsed);

        var recoveryDeadline = System.Diagnostics.Stopwatch.StartNew();
        var recovered = false;
        while (!recovered && recoveryDeadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            recovered = await SingleInstanceService.ForwardAsync(
                AppCommand.Overview,
                TimeSpan.FromSeconds(1),
                pipeName: pipeName);
            if (!recovered)
                await Task.Delay(50);
        }

        Assert.IsTrue(recovered);
    }

    [TestMethod]
    public async Task IdleClientTimesOutAndListenerAcceptsNextCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"Cerebrum.Thalamus.Tests.{suffix}";
        using var primary = new SingleInstanceService(
            $@"Local\Cerebrum.Thalamus.Tests.{suffix}",
            pipeName,
            requestReadTimeout: TimeSpan.FromMilliseconds(250));
        var handled = 0;
        primary.Start(_ =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        });

        using var idleClient = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await idleClient.ConnectAsync(2000);
        await Task.Delay(500);

        var recovered = await SingleInstanceService.ForwardAsync(
            AppCommand.Overview,
            TimeSpan.FromSeconds(2),
            pipeName: pipeName);

        Assert.IsTrue(recovered);
        Assert.AreEqual(1, handled);
    }

    [TestMethod]
    public void AbandonedMutexIsRecoveredAsPrimary()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\Cerebrum.Thalamus.Tests.{suffix}";
        Mutex? abandoned = null;
        var owner = new Thread(() => abandoned = new Mutex(true, mutexName, out _));
        owner.Start();
        Assert.IsTrue(owner.Join(TimeSpan.FromSeconds(2)));
        Assert.IsNotNull(abandoned);

        try
        {
            using var recovered = new SingleInstanceService(
                mutexName, $"Cerebrum.Thalamus.Tests.{suffix}");
            Assert.IsTrue(recovered.IsPrimary);
        }
        finally
        {
            abandoned.Dispose();
        }
    }

    [TestMethod]
    public void ListenerFaultIsReportedWithoutCrashingCaller()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var faulted = new ManualResetEventSlim();
        Exception? captured = null;
        using var service = new SingleInstanceService(
            $@"Local\Cerebrum.Thalamus.Tests.{suffix}", string.Empty);
        service.ServerFaulted = exception =>
        {
            captured = exception;
            faulted.Set();
        };

        service.Start(_ => Task.CompletedTask);

        Assert.IsTrue(faulted.Wait(TimeSpan.FromSeconds(2)));
        Assert.IsInstanceOfType<ArgumentException>(captured);
    }

    [TestMethod]
    public void DefaultPipeNameIsQualifiedByCurrentUserAndWindowsSession()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        StringAssert.Contains(
            SingleInstanceService.SessionPipeName,
            ".user-",
            StringComparison.Ordinal);
        StringAssert.Contains(
            SingleInstanceService.SessionPipeName,
            $".session-{process.SessionId}",
            StringComparison.Ordinal);
    }


}
