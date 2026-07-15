using Thalamus.Core.Commands;
using Thalamus.Core.Models;
using Thalamus.Core.Services;
using Thalamus.Services;
using Thalamus.ViewModels;

namespace Thalamus.Tests;

[TestClass]
public sealed class OverviewViewModelTests
{
    [TestMethod]
    public void NativeTrackingIsSubscribedOnlyWhileOverviewIsActive()
    {
        using var tracker = new CountingTracker();
        var viewModel = new OverviewViewModel(
            new StubWindowManager(),
            tracker,
            new StubProfileStore(),
            new UnavailableVirtualDesktopService("Unavailable"),
            new DiagnosticLog(Path.Combine(
                Path.GetTempPath(),
                "Thalamus.Tests",
                Guid.NewGuid().ToString("N"),
                "diagnostics.log")));

        Assert.AreEqual(0, tracker.SubscriberCount);
        viewModel.StartMonitoring();
        viewModel.StartMonitoring();
        Assert.AreEqual(1, tracker.SubscriberCount);

        viewModel.StopMonitoring();
        viewModel.StopMonitoring();
        Assert.AreEqual(0, tracker.SubscriberCount);

        viewModel.StartMonitoring();
        viewModel.Dispose();
        viewModel.Dispose();
        Assert.AreEqual(0, tracker.SubscriberCount);
    }

    [TestMethod]
    public async Task StoppingMonitoringCancelsActiveRefresh()
    {
        using var tracker = new CountingTracker();
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        var manager = new StubWindowManager(async cancellationToken =>
        {
            started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<WindowSnapshot>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved = true;
                throw;
            }
        });
        var viewModel = new OverviewViewModel(
            manager,
            tracker,
            new StubProfileStore(),
            new UnavailableVirtualDesktopService("Unavailable"),
            new DiagnosticLog(Path.Combine(
                Path.GetTempPath(),
                "Thalamus.Tests",
                Guid.NewGuid().ToString("N"),
                "diagnostics.log")));

        viewModel.StartMonitoring();
        var refresh = viewModel.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.StopMonitoring();
        await refresh.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(cancellationObserved);
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task BoundsNonVirtualizedOverviewCards()
    {
        using var tracker = new CountingTracker();
        var snapshots = Enumerable.Range(1, 300)
            .Select(index => new WindowSnapshot(
                index,
                index,
                "application",
                "MainWindow",
                $"Window {index:D3}",
                new RectI(10, 10, 800, 600),
                "DISPLAY1",
                false,
                false))
            .ToArray();
        var viewModel = new OverviewViewModel(
            new StubWindowManager(_ =>
                Task.FromResult<IReadOnlyList<WindowSnapshot>>(snapshots)),
            tracker,
            new StubProfileStore(),
            new UnavailableVirtualDesktopService("Unavailable"),
            new DiagnosticLog(Path.Combine(
                Path.GetTempPath(),
                "Thalamus.Tests",
                Guid.NewGuid().ToString("N"),
                "diagnostics.log")));

        viewModel.StartMonitoring();
        await viewModel.RefreshAsync();

        Assert.HasCount(256, viewModel.Windows);
        Assert.AreEqual("Showing 256 of 300 windows", viewModel.Status);
        viewModel.Dispose();
    }

    [TestMethod]
    public void SelectionNavigationWrapsAndHandlesExtremeDeltas()
    {
        using var tracker = new CountingTracker();
        using var viewModel = CreateViewModel(tracker);
        foreach (var index in Enumerable.Range(1, 3))
        {
            viewModel.Windows.Add(new WindowCardViewModel(
                new WindowSnapshot(
                    index,
                    index,
                    "application",
                    "MainWindow",
                    $"Window {index}",
                    new RectI(10, 10, 800, 600),
                    "DISPLAY1",
                    false,
                    false),
                "application",
                null));
        }

        viewModel.SelectedIndex = 0;
        viewModel.MoveSelection(-1);
        Assert.AreEqual(2, viewModel.SelectedIndex);

        viewModel.MoveSelection(1);
        Assert.AreEqual(0, viewModel.SelectedIndex);

        viewModel.MoveSelection(int.MinValue);
        Assert.AreEqual(1, viewModel.SelectedIndex);

        viewModel.MoveSelection(int.MaxValue);
        Assert.AreEqual(2, viewModel.SelectedIndex);
    }

    [TestMethod]
    public void SelectionNavigationIsSafeWhenOverviewIsEmpty()
    {
        using var tracker = new CountingTracker();
        using var viewModel = CreateViewModel(tracker);

        viewModel.MoveSelection(1);

        Assert.AreEqual(-1, viewModel.SelectedIndex);
        Assert.IsNull(viewModel.SelectedWindow);
    }

    [TestMethod]
    public async Task MissingLayoutReportsFailure()
    {
        using var tracker = new CountingTracker();
        var viewModel = new OverviewViewModel(
            new StubWindowManager(),
            tracker,
            new StubProfileStore(),
            new UnavailableVirtualDesktopService("Unavailable"),
            new DiagnosticLog(Path.Combine(
                Path.GetTempPath(),
                "Thalamus.Tests",
                Guid.NewGuid().ToString("N"),
                "diagnostics.log")));

        var restored = await viewModel.RestoreLayoutAsync("missing");

        Assert.IsFalse(restored);
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task SaveLayoutUsesOneTopologySnapshotAndSkipsUnmappedWindows()
    {
        using var tracker = new CountingTracker();
        var monitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            96,
            96,
            true);
        var windows = new[]
        {
            new WindowSnapshot(
                41,
                7,
                "application",
                "MainWindow",
                "Mapped",
                new RectI(100, 80, 800, 600),
                monitor.DeviceName,
                false,
                true),
            new WindowSnapshot(
                42,
                8,
                "other",
                "MainWindow",
                "Unmapped",
                new RectI(200, 120, 700, 500),
                "REMOVED-DISPLAY",
                false,
                false)
        };
        var manager = new StubWindowManager(
            _ => Task.FromResult<IReadOnlyList<WindowSnapshot>>(windows),
            [monitor]);
        var store = new StubProfileStore();
        using var viewModel = new OverviewViewModel(
            manager,
            tracker,
            store,
            new UnavailableVirtualDesktopService("Unavailable"),
            CreateLog());

        await viewModel.SaveLayoutAsync("work");

        Assert.IsNotNull(store.SavedProfile);
        Assert.AreEqual("work", store.SavedProfile.Name);
        Assert.HasCount(1, store.SavedProfile.Windows);
        Assert.AreEqual("application", store.SavedProfile.Windows[0].ApplicationId);
        Assert.IsTrue(store.SavedProfile.Windows[0].WasMaximized);
        Assert.AreEqual(1, manager.MonitorRequestCount);
        Assert.AreEqual("Saved layout \u201cwork\u201d", viewModel.Status);
    }

    [TestMethod]
    public async Task RestoreLayoutAppliesMatchedPlacementsAndReportsResult()
    {
        using var tracker = new CountingTracker();
        var monitor = new MonitorSnapshot(
            "DISPLAY1",
            new RectI(0, 0, 1920, 1080),
            new RectI(0, 0, 1920, 1040),
            96,
            96,
            true);
        var savedBounds = new RectI(100, 80, 800, 600);
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            "work",
            DateTimeOffset.UtcNow,
            [monitor],
            [new WindowPlacementRecord(
                "application",
                "MainWindow",
                0,
                savedBounds,
                monitor.DeviceName,
                true)]);
        var windows = new[]
        {
            new WindowSnapshot(
                42,
                7,
                "application",
                "MainWindow",
                "Window",
                savedBounds,
                monitor.DeviceName,
                false,
                false)
        };
        var applied = new List<PlannedPlacement>();
        var manager = new StubWindowManager(
            _ => Task.FromResult<IReadOnlyList<WindowSnapshot>>(windows),
            [monitor],
            (handle, bounds, maximize) =>
            {
                applied.Add(new PlannedPlacement(handle, bounds, maximize));
                return true;
            });
        using var viewModel = new OverviewViewModel(
            manager,
            tracker,
            new StubProfileStore(profile),
            new UnavailableVirtualDesktopService("Unavailable"),
            CreateLog());

        var restored = await viewModel.RestoreLayoutAsync("work");

        Assert.IsTrue(restored);
        Assert.HasCount(1, applied);
        Assert.AreEqual(42L, applied[0].Handle);
        Assert.AreEqual(savedBounds, applied[0].Bounds);
        Assert.IsTrue(applied[0].MaximizeAfterPlacement);
        Assert.AreEqual("Restored 1 of 1 windows", viewModel.Status);
    }

    private static OverviewViewModel CreateViewModel(CountingTracker tracker) =>
        new(
            new StubWindowManager(),
            tracker,
            new StubProfileStore(),
            new UnavailableVirtualDesktopService("Unavailable"),
            new DiagnosticLog(Path.Combine(
                Path.GetTempPath(),
                "Thalamus.Tests",
                Guid.NewGuid().ToString("N"),
                "diagnostics.log")));

    private static DiagnosticLog CreateLog() =>
        new(Path.Combine(
            Path.GetTempPath(),
            "Thalamus.Tests",
            Guid.NewGuid().ToString("N"),
            "diagnostics.log"));

    private sealed class CountingTracker : IWindowTracker
    {
        private EventHandler? _windowsChanged;

        public int SubscriberCount { get; private set; }

        public event EventHandler? WindowsChanged
        {
            add
            {
                _windowsChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _windowsChanged -= value;
                SubscriberCount--;
            }
        }

        public void Start()
        {
        }

        public void RequestRefresh()
        {
        }

        public void Dispose()
        {
            _windowsChanged = null;
            SubscriberCount = 0;
        }
    }

    private sealed class StubWindowManager : IWindowManager
    {
        private readonly Func<CancellationToken, Task<IReadOnlyList<WindowSnapshot>>>? _getWindows;
        private readonly IReadOnlyList<MonitorSnapshot> _monitors;
        private readonly Func<long, RectI, bool, bool>? _applyPlacement;

        internal int MonitorRequestCount { get; private set; }

        internal StubWindowManager(
            Func<CancellationToken, Task<IReadOnlyList<WindowSnapshot>>>? getWindows = null,
            IReadOnlyList<MonitorSnapshot>? monitors = null,
            Func<long, RectI, bool, bool>? applyPlacement = null)
        {
            _getWindows = getWindows;
            _monitors = monitors ?? [];
            _applyPlacement = applyPlacement;
        }

        public Task<IReadOnlyList<WindowSnapshot>> GetWindowsAsync(
            CancellationToken cancellationToken = default) => _getWindows?.Invoke(cancellationToken) ??
            Task.FromResult<IReadOnlyList<WindowSnapshot>>([]);

        public IReadOnlyList<MonitorSnapshot> GetMonitors()
        {
            MonitorRequestCount++;
            return _monitors;
        }

        public long GetForegroundWindow() => 0;
        public bool Activate(long handle) => false;
        public bool Minimize(long handle) => false;
        public Task<bool> CloseAsync(long handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public bool Tile(long handle, TileTarget target) => false;
        public bool MoveToMonitor(long handle, string monitorDeviceName) => false;
        public bool ApplyPlacement(long handle, RectI bounds, bool maximizeAfterPlacement) =>
            _applyPlacement?.Invoke(handle, bounds, maximizeAfterPlacement) ?? false;
    }

    private sealed class StubProfileStore : ILayoutProfileStore
    {
        private readonly LayoutProfile? _profile;

        internal LayoutProfile? SavedProfile { get; private set; }

        internal StubProfileStore(LayoutProfile? profile = null)
        {
            _profile = profile;
        }

        public Task SaveAsync(
            LayoutProfile profile,
            CancellationToken cancellationToken = default)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }

        public Task<LayoutProfile?> LoadAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_profile);
    }
}
