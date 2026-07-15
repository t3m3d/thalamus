using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Thalamus.Core.Commands;
using Thalamus.Core.Layout;
using Thalamus.Core.Models;
using Thalamus.Core.Services;
using Thalamus.Interop;

namespace Thalamus.Services;

internal sealed class WindowManager : IWindowManager
{
    private readonly ConcurrentDictionary<long, SavedPlacement> _placements = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private sealed record SavedPlacement(
        RectI Bounds,
        bool WasMaximized,
        bool WasMinimized,
        int ProcessId,
        string ClassName);

    public Task<IReadOnlyList<WindowSnapshot>> GetWindowsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<WindowSnapshot>>(() => EnumerateWindows(cancellationToken), cancellationToken);

    public IReadOnlyList<MonitorSnapshot> GetMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                DeviceName = string.Empty
            };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
                return true;
            var bounds = ToRect(info.Monitor);
            var workArea = ToRect(info.Work);
            if (!bounds.IsValid || !workArea.IsValid)
                return true;


            uint dpiX = 96;
            uint dpiY = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x > 0 && y > 0)
                {
                    dpiX = x;
                    dpiY = y;
                }
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException)
            {
                // Windows 8.0 fallback.
            }

            monitors.Add(new MonitorSnapshot(
                info.DeviceName,
                bounds,
                workArea,
                dpiX,
                dpiY,
                (info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            return true;
        };

        var completed = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return completed ? monitors : [];
    }

    public long GetForegroundWindow()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        return IsSafeTarget(hWnd) ? hWnd.ToInt64() : 0;
    }

    public bool Activate(long handle)
    {
        var hWnd = new IntPtr(handle);
        if (!IsSafeTarget(hWnd))
            return false;

        var restored = !NativeMethods.IsIconic(hWnd) ||
            NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
        return restored && NativeMethods.SetForegroundWindow(hWnd);
    }

    public bool Minimize(long handle)
    {
        var hWnd = new IntPtr(handle);
        return IsSafeTarget(hWnd) &&
            NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MINIMIZE);
    }

    public Task<bool> CloseAsync(long handle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hWnd = new IntPtr(handle);
        var accepted = IsSafeTarget(hWnd) &&
            NativeMethods.PostMessageW(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return Task.FromResult(accepted);
    }

    public bool Tile(long handle, TileTarget target)
    {
        var hWnd = new IntPtr(handle);
        if (!IsSafeTarget(hWnd) || !Enum.IsDefined(target))
            return false;

        if (target == TileTarget.Restore)
        {
            if (_placements.TryGetValue(handle, out var saved))
            {
                if (!PlacementMatches(hWnd, saved))
                {
                    _placements.TryRemove(handle, out _);
                    return NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
                }

                var accepted = NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
                accepted &= SetBounds(hWnd, MonitorGeometry.EnsureVisible(saved.Bounds, GetMonitors()));
                if (saved.WasMaximized)
                    accepted &= NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MAXIMIZE);
                else if (saved.WasMinimized)
                    accepted &= NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MINIMIZE);
                if (accepted)
                    _placements.TryRemove(handle, out _);
                return accepted;
            }

            return NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
        }

        RememberPlacement(hWnd);
        if (target == TileTarget.Maximize)
            return NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MAXIMIZE);

        var monitor = MonitorForWindow(hWnd);
        if (monitor is null)
            return false;

        var targetBounds = SnapLayout.Calculate(monitor.WorkArea, target);
        var restored = NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
        return SetBounds(hWnd, targetBounds) && restored;
    }

    public bool MoveToMonitor(long handle, string monitorDeviceName)
    {
        var hWnd = new IntPtr(handle);
        if (!IsSafeTarget(hWnd))
            return false;

        var wasMaximized = NativeMethods.IsZoomed(hWnd);
        var wasMinimized = NativeMethods.IsIconic(hWnd);
        if (!TryGetRestorableRect(hWnd, out var bounds))
            return false;

        var monitors = GetMonitors();
        var source = MonitorGeometry.BestMonitor(bounds, monitors);
        var target = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null)
            return false;

        RectI mapped;
        try
        {
            mapped = MonitorGeometry.MapBetweenMonitors(bounds, source, target);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException)
        {
            return false;
        }

        RememberPlacement(hWnd);
        var accepted = NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
        accepted &= SetBounds(hWnd, mapped);
        if (accepted && wasMaximized)
            accepted &= NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MAXIMIZE);
        else if (accepted && wasMinimized)
            accepted &= NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MINIMIZE);
        return accepted;
    }

    public bool ApplyPlacement(long handle, RectI bounds, bool maximizeAfterPlacement)
    {
        var hWnd = new IntPtr(handle);
        if (!IsSafeTarget(hWnd) || !bounds.IsValid)
            return false;

        RememberPlacement(hWnd);
        var accepted = NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
        accepted &= SetBounds(hWnd, bounds);
        if (!accepted)
            return false;

        if (maximizeAfterPlacement)
            accepted &= NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_MAXIMIZE);

        return accepted;
    }


    private bool IsSafeTarget(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd))
            return false;

        if (NativeMethods.GetWindowThreadProcessId(hWnd, out var processId) == 0)
            return false;
        var className = ReadClassName(hWnd);
        return processId != 0 &&
            processId != _ownProcessId &&
            !string.IsNullOrWhiteSpace(className) &&
            !WindowRules.IsProtectedClass(className);
    }
    private IReadOnlyList<WindowSnapshot> EnumerateWindows(CancellationToken cancellationToken)
    {
        var windows = new List<WindowSnapshot>();
        var liveHandles = new HashSet<long>();
        var applicationIds = new Dictionary<int, string>();

        var enumerationSucceeded = NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (NativeMethods.GetWindowThreadProcessId(hWnd, out var processId) == 0)
                return true;
            if (processId == _ownProcessId)
                return true;
            if (processId > int.MaxValue)
                return true;
            liveHandles.Add(hWnd.ToInt64());

            if (!TryGetRestorableRect(hWnd, out var bounds))
                return true;

            var candidate = new NativeWindowCandidate(
                hWnd.ToInt64(),
                NativeMethods.IsWindowVisible(hWnd),
                NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) == hWnd,
                IsCloaked(hWnd),
                (NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64() &
                    NativeMethods.WS_EX_TOOLWINDOW) != 0,
                NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero,
                (NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64() &
                    NativeMethods.WS_EX_NOACTIVATE) != 0,
                ReadClassName(hWnd),
                ReadTitle(hWnd),
                bounds,
                (int)processId);

            if (!WindowRules.IsEligible(candidate))
                return true;

            if (!applicationIds.TryGetValue(candidate.ProcessId, out var applicationId))
            {
                applicationId = ReadApplicationId(candidate.ProcessId);
                applicationIds[candidate.ProcessId] = applicationId;
            }


            var monitor = MonitorForWindow(hWnd);
            windows.Add(new WindowSnapshot(
                candidate.Handle,
                candidate.ProcessId,
                applicationId,
                candidate.ClassName,
                candidate.Title,
                candidate.Bounds,
                monitor?.DeviceName ?? string.Empty,
                NativeMethods.IsIconic(hWnd),
                NativeMethods.IsZoomed(hWnd)));
            return true;
        }, IntPtr.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        if (!enumerationSucceeded)
            throw new InvalidOperationException("Win32 window enumeration failed.");


        foreach (var stale in _placements.Keys.Where(handle => !liveHandles.Contains(handle)))
            _placements.TryRemove(stale, out _);

        return windows
            .OrderBy(window => window.MonitorDeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Handle).ToArray();
    }

    private static MonitorSnapshot? MonitorForWindow(IntPtr hWnd)
    {
        var monitorHandle = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitorHandle == IntPtr.Zero)
            return null;

        var info = new NativeMethods.MONITORINFOEX
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            DeviceName = string.Empty
        };
        if (!NativeMethods.GetMonitorInfoW(monitorHandle, ref info))
            return null;
        var bounds = ToRect(info.Monitor);
        var workArea = ToRect(info.Work);
        if (!bounds.IsValid || !workArea.IsValid)
            return null;


        uint dpi = 96;
        try
        {
            dpi = NativeMethods.GetDpiForWindow(hWnd);
            if (dpi == 0)
                dpi = 96;
        }
        catch (EntryPointNotFoundException)
        {
            dpi = 96;
        }

        return new MonitorSnapshot(
            info.DeviceName,
            bounds,
            workArea,
            dpi,
            dpi,
            (info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0);
    }

    private void RememberPlacement(IntPtr hWnd)
    {
        var handle = hWnd.ToInt64();
        if (_placements.ContainsKey(handle) || !TryGetRestorableRect(hWnd, out var bounds))
            return;

        if (NativeMethods.GetWindowThreadProcessId(hWnd, out var processId) == 0 ||
            processId is 0 || processId > int.MaxValue)
            return;

        _placements.TryAdd(handle, new SavedPlacement(
            bounds,
            NativeMethods.IsZoomed(hWnd),
            NativeMethods.IsIconic(hWnd),
            (int)processId,
            ReadClassName(hWnd)));
    }

    private static bool PlacementMatches(IntPtr hWnd, SavedPlacement saved)
    {
        return NativeMethods.GetWindowThreadProcessId(hWnd, out var processId) != 0 &&
            processId == saved.ProcessId &&
            string.Equals(ReadClassName(hWnd), saved.ClassName, StringComparison.Ordinal);
    }

    private static bool SetBounds(IntPtr hWnd, RectI bounds) =>
        NativeMethods.SetWindowPos(
            hWnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_ASYNCWINDOWPOS);


    private static bool TryGetRestorableRect(IntPtr hWnd, out RectI bounds)
    {
        if (NativeMethods.IsZoomed(hWnd) || NativeMethods.IsIconic(hWnd))
        {
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                Length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
            };
            if (NativeMethods.GetWindowPlacement(hWnd, ref placement))
            {
                bounds = ToRect(placement.NormalPosition);
                if (bounds.IsValid)
                {
                    var usesWorkspaceCoordinates =
                        NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) == hWnd &&
                        (NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64() &
                            NativeMethods.WS_EX_TOOLWINDOW) == 0;
                    var monitorHandle = NativeMethods.MonitorFromWindow(
                        hWnd,
                        NativeMethods.MONITOR_DEFAULTTONEAREST);
                    var monitorInfo = new NativeMethods.MONITORINFOEX
                    {
                        Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                        DeviceName = string.Empty
                    };
                    if (usesWorkspaceCoordinates &&
                        monitorHandle != IntPtr.Zero &&
                        NativeMethods.GetMonitorInfoW(monitorHandle, ref monitorInfo))
                    {
                        var monitorBounds = ToRect(monitorInfo.Monitor);
                        var workArea = ToRect(monitorInfo.Work);
                        if (monitorBounds.IsValid && workArea.IsValid)
                        {
                            try
                            {
                                bounds = MonitorGeometry.WorkspaceToScreen(
                                    bounds,
                                    monitorBounds,
                                    workArea);
                            }
                            catch (OverflowException)
                            {
                                // The raw placement remains a safer fallback than the current iconic bounds.
                            }
                        }
                    }

                    return true;
                }
            }
        }

        return TryGetRect(hWnd, out bounds);
    }

    private static bool TryGetRect(IntPtr hWnd, out RectI bounds)
    {
        bounds = default;
        if (!NativeMethods.GetWindowRect(hWnd, out var native))
            return false;

        bounds = ToRect(native);
        return bounds.IsValid;
    }

    private static RectI ToRect(NativeMethods.RECT rect)
    {
        var width = (long)rect.Right - rect.Left;
        var height = (long)rect.Bottom - rect.Top;
        if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue)
            return default;

        return new RectI(rect.Left, rect.Top, (int)width, (int)height);
    }

    private static bool IsCloaked(IntPtr hWnd) =>
        NativeMethods.DwmGetWindowAttribute(
            hWnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ReadTitle(IntPtr hWnd)
    {
        var length = Math.Clamp(NativeMethods.GetWindowTextLengthW(hWnd) + 1, 2, 4096);
        var builder = new StringBuilder(length);
        var copied = NativeMethods.GetWindowTextW(hWnd, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : string.Empty;
    }

    private static string ReadClassName(IntPtr hWnd)
    {
        var builder = new StringBuilder(256);
        var copied = NativeMethods.GetClassNameW(hWnd, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : string.Empty;
    }

    private static string ReadApplicationId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"pid:{processId}";
        }
    }
}
