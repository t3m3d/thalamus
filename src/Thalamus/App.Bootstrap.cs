using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Thalamus.Core.Commands;
using Thalamus.Core.Models;
using Thalamus.Core.Persistence;
using Thalamus.Core.Services;
using Thalamus.Interop;
using Thalamus.Services;

namespace Thalamus;

public partial class App
{
    internal static TimeSpan CommandForwardTimeout { get; } = TimeSpan.FromSeconds(15);
    private SingleInstanceService? _singleInstance;
    private WindowTracker? _tracker;
    private VirtualDesktopService? _virtualDesktops;
    private OverviewCoordinator? _coordinator;
    private IHotkeyService? _hotkey;
    private DiagnosticLog _log = new(Path.Combine(
        Path.GetTempPath(),
        "Cerebrum",
        "Thalamus",
        "bootstrap-diagnostics.log"));

    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch (EntryPointNotFoundException)
        {
            // WPF remains system-DPI aware on older supported builds.
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            _log = new DiagnosticLog();
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or InvalidOperationException or
            NotSupportedException or TypeInitializationException or
            UnauthorizedAccessException)
        {
            _log.Write("THA-DATA-ROOT-INVALID", exception.GetType().Name);
            Shutdown(2);
            return;
        }

        var parsed = CommandParser.Parse(e.Args);
        if (!parsed.Success || parsed.Command is null)
        {
            _log.Write("THA-COMMAND-INVALID");
            Shutdown(2);
            return;
        }

        _singleInstance = new SingleInstanceService();
        _singleInstance.ServerFaulted = exception =>
            _log.Write("THA-IPC-SERVER-FAILED", exception.GetType().Name);
        if (!_singleInstance.IsPrimary)
        {
            var forwarded = await SingleInstanceService.ForwardAsync(
                parsed.Command,
                CommandForwardTimeout).ConfigureAwait(true);
            if (!forwarded)
                _log.Write("THA-FORWARD-FAILED");

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(forwarded ? 0 : 3);
            return;
        }

        ThalamusSettings settings;
        try
        {
            var settingsResult = await Task.Run(async () =>
            {
                Directory.CreateDirectory(AppPaths.Root);
                var settingsFile = new AtomicJsonFile<ThalamusSettings>(
                    AppPaths.SettingsFile, maximumFileLength: 1024 * 1024);
                var loadedSettings = await settingsFile.LoadAsync().ConfigureAwait(false);
                var normalized = SettingsValidator.Normalize(loadedSettings);
                if (SettingsValidator.ShouldPersistNormalization(loadedSettings, normalized))
                    await settingsFile.RepairAsync(normalized).ConfigureAwait(false);
                var unsupportedVersion = loadedSettings is not null &&
                    loadedSettings.Version != ThalamusSettings.CurrentVersion
                        ? loadedSettings.Version
                        : (int?)null;
                return (Settings: normalized, UnsupportedVersion: unsupportedVersion);
            }).ConfigureAwait(true);
            settings = settingsResult.Settings;
            if (settingsResult.UnsupportedVersion is int unsupportedVersion)
                _log.Write(
                    "THA-SETTINGS-VERSION-UNSUPPORTED",
                    unsupportedVersion.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            settings = new ThalamusSettings();
            _log.Write("THA-SETTINGS-RECOVERED", exception.GetType().Name);
        }

        var windowManager = new WindowManager();
        _tracker = new WindowTracker();
        _virtualDesktops = await Task.Run(
            () => new VirtualDesktopService()).ConfigureAwait(true);
        var profiles = new LayoutProfileStore(AppPaths.Profiles);
        _coordinator = new OverviewCoordinator(
            windowManager,
            _tracker,
            profiles,
            _virtualDesktops,
            _log,
            settings);
        _tracker.Start();
        if (!_tracker.HasAllNativeHooks)
            _log.Write("THA-WINEVENT-UNAVAILABLE");
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _hotkey = new HotkeyService(
            settings.OverviewHotkey,
            OnOverviewHotkeyRequested);
        if (!_hotkey.IsRegistered)
            _log.Write("THA-HOTKEY-UNAVAILABLE");

        _singleInstance.Start(_coordinator.ExecuteAsync);
        _log.Write("THA-STARTED");
        await _coordinator.ExecuteAsync(parsed.Command).ConfigureAwait(true);
    }


    private async void OnOverviewHotkeyRequested()
    {
        var coordinator = _coordinator;
        if (coordinator is null)
            return;

        try
        {
            await coordinator.ExecuteAsync(AppCommand.Overview).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log.Write("THA-HOTKEY-COMMAND-FAILED", exception.GetType().Name);
        }
    }


    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _log.Write("THA-UNHANDLED", e.Exception.GetType().Name);
        if (!Dispatcher.HasShutdownStarted &&
            !Dispatcher.HasShutdownFinished &&
            NativeMethods.GetForegroundWindow() != IntPtr.Zero)
        {
            try
            {
                MessageBox.Show(
                    "Thalamus encountered an unexpected error and will shut down safely. See diagnostics.log for the event code.",
                    "Thalamus",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception dialogException)
            {
                _log.Write("THA-ERROR-DIALOG-FAILED", dialogException.GetType().Name);
            }
        }

        Shutdown(1);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _tracker?.RequestRefresh();
        _log.Write("THA-DISPLAY-CHANGED");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _tracker?.RequestRefresh();
        _log.Write(e.Mode == PowerModes.Resume ? "THA-RESUMED" : "THA-POWER-CHANGE");
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _log.Write("THA-SESSION-ENDING");
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var hotkey = _hotkey;
        var singleInstance = _singleInstance;
        var coordinator = _coordinator;
        var tracker = _tracker;
        var virtualDesktops = _virtualDesktops;
        var wasPrimary = singleInstance?.IsPrimary == true;

        _hotkey = null;
        _singleInstance = null;
        _coordinator = null;
        _tracker = null;
        _virtualDesktops = null;

        CleanupSafely(
            () => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged,
            "THA-DISPLAY-UNSUBSCRIBE-FAILED");
        CleanupSafely(
            () => SystemEvents.PowerModeChanged -= OnPowerModeChanged,
            "THA-POWER-UNSUBSCRIBE-FAILED");
        CleanupSafely(
            () => DispatcherUnhandledException -= OnDispatcherUnhandledException,
            "THA-DISPATCHER-UNSUBSCRIBE-FAILED");
        DisposeSafely(hotkey, "THA-HOTKEY-DISPOSE-FAILED");
        DisposeSafely(singleInstance, "THA-IPC-DISPOSE-FAILED");
        DisposeSafely(coordinator, "THA-OVERVIEW-DISPOSE-FAILED");
        DisposeSafely(tracker, "THA-TRACKER-DISPOSE-FAILED");
        DisposeSafely(virtualDesktops, "THA-WORKSPACE-DISPOSE-FAILED");
        if (wasPrimary)
            _log.Write("THA-STOPPED");
        _log.Flush(TimeSpan.FromSeconds(1));
        base.OnExit(e);
    }

    private void CleanupSafely(Action cleanup, string eventCode)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _log.Write(eventCode, exception.GetType().Name);
        }
    }

    private void DisposeSafely(IDisposable? resource, string eventCode)
    {
        if (resource is null)
            return;

        CleanupSafely(resource.Dispose, eventCode);
    }
}
