using Thalamus.Core.Services;
using Thalamus.Interop;

namespace Thalamus.Services;

internal sealed class WindowTracker : IWindowTracker
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly Timer _debounce;
    private readonly Timer _reconciliation;
    private readonly List<IntPtr> _hooks = [];
    private bool _started;
    private volatile bool _disposed;

    public WindowTracker()
    {
        _callback = OnWinEvent;
        _debounce = new Timer(_ => RaiseChanged());
        _reconciliation = new Timer(_ => RaiseChanged());
    }

    public event EventHandler? WindowsChanged;
    internal bool HasAllNativeHooks => _hooks.Count == 4;

    public void RequestRefresh() => ScheduleRefresh(TimeSpan.Zero);

    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND);
        AddHook(NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH, NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH);
        AddHook(NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_NAMECHANGE);
        AddHook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED);
        _reconciliation.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void AddHook(uint first, uint last)
    {
        var hook = NativeMethods.SetWinEventHook(
            first, last, IntPtr.Zero, _callback, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero)
            _hooks.Add(hook);
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hWnd,
        int objectId,
        int childId,
        uint threadId,
        uint eventTime)
    {
        if (_disposed || !IsRefreshEvent(eventType, hWnd, objectId))
            return;

        ScheduleRefresh(TimeSpan.FromMilliseconds(160));
    }

    internal static bool IsRefreshEvent(uint eventType, IntPtr hWnd, int objectId)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH)
            return true;
        if (hWnd == IntPtr.Zero)
            return false;
        if (eventType is NativeMethods.EVENT_SYSTEM_FOREGROUND
            or NativeMethods.EVENT_OBJECT_CLOAKED
            or NativeMethods.EVENT_OBJECT_UNCLOAKED)
            return true;

        return objectId == NativeMethods.OBJID_WINDOW &&
            eventType is NativeMethods.EVENT_OBJECT_CREATE
                or NativeMethods.EVENT_OBJECT_DESTROY
                or NativeMethods.EVENT_OBJECT_SHOW
                or NativeMethods.EVENT_OBJECT_HIDE
                or NativeMethods.EVENT_OBJECT_LOCATIONCHANGE
                or NativeMethods.EVENT_OBJECT_NAMECHANGE;
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        if (_disposed)
            return;

        try
        {
            _debounce.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposal won the race with a native or system event.
        }
    }

    private void RaiseChanged()
    {
        if (_disposed)
            return;

        try
        {
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A consumer must not be able to terminate the native timer callback thread.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _debounce.Dispose();
        _reconciliation.Dispose();
        foreach (var hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
