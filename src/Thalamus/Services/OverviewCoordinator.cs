using System.IO;
using System.Windows;
using Thalamus.Core.Commands;
using Thalamus.Core.Services;
using Thalamus.Core.Models;
using Thalamus.ViewModels;
using Thalamus.Views;

namespace Thalamus.Services;

internal sealed class OverviewCoordinator : IDisposable
{
    private readonly IWindowManager _windowManager;
    private readonly OverviewViewModel _viewModel;
    private readonly DiagnosticLog _log;
    private readonly ThalamusSettings _settings;
    private OverviewWindow? _window;
    private int _disposed;

    internal OverviewCoordinator(
        IWindowManager windowManager,
        IWindowTracker tracker,
        ILayoutProfileStore profiles,
        IVirtualDesktopService virtualDesktops,
        DiagnosticLog log,
        ThalamusSettings settings)
    {
        _windowManager = windowManager;
        _viewModel = new OverviewViewModel(windowManager, tracker, profiles, virtualDesktops, log);
        _viewModel.DismissRequested += OnDismissRequested;
        _log = log;
        _settings = settings;
    }

    internal Task ExecuteAsync(AppCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Volatile.Read(ref _disposed) != 0)
            return Task.CompletedTask;

        var application = Application.Current;
        if (application is null ||
            application.Dispatcher.HasShutdownStarted ||
            application.Dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        var dispatcher = application.Dispatcher;
        return dispatcher.CheckAccess()
            ? ExecuteOnUiAsync(command)
            : dispatcher.InvokeAsync(() => ExecuteOnUiAsync(command)).Task.Unwrap();
    }

    private async Task ExecuteOnUiAsync(AppCommand command)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            switch (command.Kind)
            {
                case AppCommandKind.ShowOverview:
                    await ShowOverviewAsync().ConfigureAwait(true);
                    break;

                case AppCommandKind.TileActive:
                    if (!TryTile(command.Argument))
                    {
                        _viewModel.Status = "Unable to tile the active window";
                        _log.Write("THA-TILE-REJECTED");
                    }
                    break;

                case AppCommandKind.SwitchWorkspace:
                    if (!await _viewModel.SwitchWorkspaceAsync(ParseDirection(command.Argument)).ConfigureAwait(true))
                        _log.Write("THA-WORKSPACE-UNAVAILABLE");
                    break;

                case AppCommandKind.MoveActiveToWorkspace:
                    if (!await _viewModel.MoveActiveWorkspaceAsync(ParseDirection(command.Argument)).ConfigureAwait(true))
                        _log.Write("THA-WORKSPACE-UNAVAILABLE");
                    break;

                case AppCommandKind.SaveLayout:
                    await _viewModel.SaveLayoutAsync(command.Argument ?? "default").ConfigureAwait(true);
                    _log.Write("THA-LAYOUT-SAVED");
                    break;

                case AppCommandKind.RestoreLayout:
                    if (await _viewModel.RestoreLayoutAsync(command.Argument ?? "default").ConfigureAwait(true))
                        _log.Write("THA-LAYOUT-RESTORED");
                    else
                        _log.Write("THA-LAYOUT-NOT-FOUND");
                    break;

                case AppCommandKind.Exit:
                    _log.Write("THA-EXIT-REQUESTED");
                    _ = Application.Current.Dispatcher.BeginInvoke(new Action(Application.Current.Shutdown));
                    break;
            }
        }
        catch (ArgumentException)
        {
            _viewModel.Status = "Command rejected safely";
            _log.Write("THA-COMMAND-REJECTED");
        }
        catch (InvalidOperationException exception)
        {
            _viewModel.Status = "Window operation failed safely";
            _log.Write("THA-WINDOW-OPERATION-FAILED", exception.GetType().Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _viewModel.Status = "Storage operation failed safely";
            _log.Write("THA-STORAGE-FAILED");
        }
    }

    private async Task ShowOverviewAsync()
    {
        var window = _window;
        if (window is null)
        {
            window = new OverviewWindow(_viewModel, _settings);
            window.Closed += OnOverviewClosed;
            _window = window;
        }

        _viewModel.StartMonitoring();
        if (!window.IsVisible)
            window.Show();

        window.Activate();
        _log.Write("THA-OVERVIEW-SHOWN");
        await _viewModel.RefreshAsync().ConfigureAwait(true);
        if (ReferenceEquals(_window, window) && window.IsVisible)
            window.Activate();
    }

    private bool TryTile(string? argument)
    {
        if (!Enum.TryParse<TileTarget>(
                argument?.Replace("-", string.Empty, StringComparison.Ordinal),
                true,
                out var target))
            return false;

        var active = _windowManager.GetForegroundWindow();
        if (active != 0 && _windowManager.Tile(active, target))
            return true;

        if (_window?.IsActive != true)
            return false;

        var selected = _viewModel.SelectedWindow;
        return selected is not null && _windowManager.Tile(selected.Handle, target);
    }

    private static Direction ParseDirection(string? argument) =>
        string.Equals(argument, "previous", StringComparison.OrdinalIgnoreCase)
            ? Direction.Previous
            : Direction.Next;

    private void OnDismissRequested(object? sender, EventArgs e) => _window?.Close();

    private void OnOverviewClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _window))
            return;

        _window = null;
        _viewModel.StopMonitoring();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _viewModel.DismissRequested -= OnDismissRequested;
        try
        {
            if (_window is not null)
            {
                _window.Closed -= OnOverviewClosed;
                _window.Close();
            }
        }
        finally
        {
            _window = null;
            _viewModel.StopMonitoring();
            _viewModel.Dispose();
        }
    }
}
