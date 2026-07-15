using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Thalamus.Core.Commands;
using Thalamus.Core.Layout;
using Thalamus.Core.Models;
using Thalamus.Core.Services;
using Thalamus.Services;

namespace Thalamus.ViewModels;

internal sealed class OverviewViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaximumOverviewWindows = 256;
    private readonly IWindowManager _windowManager;
    private readonly IWindowTracker _tracker;
    private readonly ILayoutProfileStore _profiles;
    private readonly IVirtualDesktopService _virtualDesktops;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DiagnosticLog _log;
    private string _status = "Ready";
    private int _selectedIndex = -1;
    private volatile bool _disposed;
    private int _refreshPending;
    private volatile bool _isMonitoring;
    private CancellationTokenSource? _monitoringCancellation;

    internal OverviewViewModel(
        IWindowManager windowManager,
        IWindowTracker tracker,
        ILayoutProfileStore profiles,
        IVirtualDesktopService virtualDesktops,
        DiagnosticLog log)
    {
        _windowManager = windowManager;
        _tracker = tracker;
        _profiles = profiles;
        _virtualDesktops = virtualDesktops;
        _log = log;

        ActivateCommand = new RelayCommand(value => Activate(value as WindowCardViewModel));
        MinimizeCommand = new RelayCommand(value => Minimize(value as WindowCardViewModel));
        CloseCommand = new RelayCommand(value => _ = CloseAsync(value as WindowCardViewModel));
        TileCommand = new RelayCommand(value =>
        {
            if (value is string name && Enum.TryParse<TileTarget>(name, true, out var target))
                TileSelected(target);
        });
    }

    public ObservableCollection<WindowCardViewModel> Windows { get; } = [];
    public ICommand ActivateCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand TileCommand { get; }

    public string Status
    {
        get => _status;
        internal set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var normalized = Windows.Count == 0 ? -1 : Math.Clamp(value, 0, Windows.Count - 1);
            if (_selectedIndex == normalized)
                return;
            _selectedIndex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWindow));
        }
    }

    internal WindowCardViewModel? SelectedWindow =>
        SelectedIndex >= 0 && SelectedIndex < Windows.Count ? Windows[SelectedIndex] : null;

    internal IReadOnlyList<MonitorSnapshot> Monitors => _windowManager.GetMonitors();

    internal event EventHandler? DismissRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void StartMonitoring()
    {
        if (_disposed || _isMonitoring)
            return;

        _monitoringCancellation = new CancellationTokenSource();
        _isMonitoring = true;
        _tracker.WindowsChanged += OnWindowsChanged;
    }

    internal void StopMonitoring()
    {
        if (!_isMonitoring)
            return;

        _tracker.WindowsChanged -= OnWindowsChanged;
        _isMonitoring = false;
        var cancellation = Interlocked.Exchange(ref _monitoringCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        Interlocked.Exchange(ref _refreshPending, 0);
    }

    internal async Task RefreshAsync()
    {
        if (_disposed)
            return;

        var cancellationToken = _monitoringCancellation?.Token ?? CancellationToken.None;
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(true))
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            return;
        }

        try
        {
            var selectedHandle = SelectedWindow?.Handle;
            Status = "Scanning windows…";
            var snapshots = await _windowManager.GetWindowsAsync(cancellationToken).ConfigureAwait(true);
            var groups = WindowGrouping.Group(snapshots)
                .SelectMany(group => group.Windows.Select(window => (window, label:
                    group.Windows.Count > 1
                        ? $"{group.ApplicationId} · {group.Windows.Count} windows"
                        : group.ApplicationId)))
                .Take(MaximumOverviewWindows)
                .ToArray();

            var cards = await Task.Run(() => groups
                .AsParallel()
                .AsOrdered()
                .WithCancellation(cancellationToken)
                .WithDegreeOfParallelism(Math.Clamp(Environment.ProcessorCount, 1, 8))
                .Select(item => new WindowCardViewModel(
                    item.window,
                    item.label,
                    IconService.GetIcon(item.window)))
                .ToArray(), cancellationToken).ConfigureAwait(true);

            Windows.Clear();
            foreach (var card in cards)
                Windows.Add(card);
            var previousIndex = selectedHandle.HasValue
                ? Array.FindIndex(cards, card => card.Handle == selectedHandle.Value)
                : -1;
            SelectedIndex = cards.Length == 0 ? -1 : Math.Max(0, previousIndex);
            Status = Windows.Count == 0
                ? "No eligible application windows"
                : snapshots.Count > Windows.Count
                    ? $"Showing {Windows.Count} of {snapshots.Count} windows"
                    : $"{Windows.Count} windows";
        }
        catch (OperationCanceledException)
        {
            // Closing the overview or application cancels in-flight refreshes.
        }
        catch (Exception exception)
        {
            Status = "Window scan failed safely";
            _log.Write("THA-WINDOW-SCAN-FAILED", exception.GetType().Name);
        }
        finally
        {
            _refreshGate.Release();
        }

        if (!_disposed && _isMonitoring &&
            Interlocked.Exchange(ref _refreshPending, 0) == 1)
            await RefreshAsync().ConfigureAwait(true);
    }


    internal void MoveSelection(int delta)
    {
        if (Windows.Count == 0)
            return;

        var count = (long)Windows.Count;
        var candidate = ((SelectedIndex + (long)delta) % count + count) % count;
        SelectedIndex = checked((int)candidate);
    }

    internal void ActivateSelected() => Activate(SelectedWindow);

    internal void TileSelected(TileTarget target)
    {
        var selected = SelectedWindow;
        if (selected is null)
        {
            Status = "No window is selected";
            return;
        }

        Status = _windowManager.Tile(selected.Handle, target)
            ? $"Applied {target}"
            : "The window rejected that placement";
    }

    internal void MoveToMonitor(WindowCardViewModel card, string deviceName)
    {
        Status = _windowManager.MoveToMonitor(card.Handle, deviceName)
            ? "Moved window to monitor"
            : "Unable to move that window";
        _ = RefreshAsync();
    }

    internal void MoveSelectedToAdjacentMonitor(int offset)
    {
        var selected = SelectedWindow;
        if (selected is null)
        {
            Status = "No window is selected";
            return;
        }

        var target = MonitorGeometry.AdjacentMonitor(
            _windowManager.GetMonitors(), selected.MonitorDeviceName, offset);
        if (target is null)
        {
            Status = "No other monitor is available";
            return;
        }

        MoveToMonitor(selected, target.DeviceName);
    }


    internal async Task SaveLayoutAsync(string name)
    {
        var windows = await _windowManager.GetWindowsAsync().ConfigureAwait(true);
        var monitors = _windowManager.GetMonitors();
        var monitorNames = monitors
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = windows
            .Where(window => monitorNames.Contains(window.MonitorDeviceName))
            .GroupBy(w => $"{w.ApplicationId}\u001f{w.ClassName}",
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Handle)
                .Select((window, index) =>
                    new WindowPlacementRecord(
                        window.ApplicationId,
                        window.ClassName,
                        index,
                        window.Bounds,
                        window.MonitorDeviceName,
                        window.IsMaximized)))
            .Take(10_000)
            .ToArray();
        var profile = new LayoutProfile(
            LayoutProfile.CurrentVersion,
            name,
            DateTimeOffset.UtcNow,
            monitors,
            records);
        await Task.Run(() => _profiles.SaveAsync(profile)).ConfigureAwait(true);
        Status = $"Saved layout “{name}”";
    }

    internal async Task<bool> RestoreLayoutAsync(string name)
    {
        var profile = await Task.Run(() => _profiles.LoadAsync(name)).ConfigureAwait(true);
        if (profile is null)
        {
            Status = $"Layout “{name}” was not found";
            return false;
        }

        var windows = await _windowManager.GetWindowsAsync().ConfigureAwait(true);
        var plan = LayoutRestorePlanner.Plan(profile, windows, _windowManager.GetMonitors());
        var applied = await Task.Run(() => plan.Count(item =>
            _windowManager.ApplyPlacement(item.Handle, item.Bounds, item.MaximizeAfterPlacement)))
            .ConfigureAwait(true);
        Status = $"Restored {applied} of {plan.Count} windows";
        if (_isMonitoring)
            await RefreshAsync().ConfigureAwait(true);
        return true;
    }

    internal async Task<bool> SwitchWorkspaceAsync(Direction direction)
    {
        var success = await _virtualDesktops.SwitchAsync(direction).ConfigureAwait(true);
        Status = success ? "Switched workspace" : _virtualDesktops.Capabilities.Explanation;
        return success;
    }

    internal async Task<bool> MoveActiveWorkspaceAsync(Direction direction)
    {
        var active = _windowManager.GetForegroundWindow();
        var success = active != 0 &&
            await _virtualDesktops.MoveWindowAsync(active, direction).ConfigureAwait(true);
        Status = success ? "Moved window to workspace" : _virtualDesktops.Capabilities.Explanation;
        return success;
    }

    private void Activate(WindowCardViewModel? card)
    {
        if (card is null)
            return;

        if (_windowManager.Activate(card.Handle))
            DismissRequested?.Invoke(this, EventArgs.Empty);
        else
            Status = "Unable to activate that window";
    }

    private void Minimize(WindowCardViewModel? card)
    {
        if (card is null)
            return;

        var accepted = _windowManager.Minimize(card.Handle);
        Status = accepted ? "Minimized window" : "Unable to minimize that window";
        if (accepted)
            _ = RefreshAsync();
    }

    private async Task CloseAsync(WindowCardViewModel? card)
    {
        if (card is null)
            return;

        var accepted = await _windowManager.CloseAsync(card.Handle).ConfigureAwait(true);
        Status = accepted ? "Close request sent" : "Unable to close that window";
        if (accepted && _isMonitoring)
        {
            await Task.Delay(120).ConfigureAwait(true);
            if (_isMonitoring)
                await RefreshAsync().ConfigureAwait(true);
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        if (_disposed || !_isMonitoring)
            return;

        var application = Application.Current;
        if (application is null)
            return;

        try
        {
            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed && _isMonitoring)
                    _ = RefreshAsync();
            }));
        }
        catch (InvalidOperationException)
        {
            // Dispatcher shutdown won the race with a native tracker callback.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopMonitoring();
        _monitoringCancellation?.Dispose();
        _monitoringCancellation = null;
        if (_refreshGate.Wait(0))
            _refreshGate.Dispose();
    }
}
