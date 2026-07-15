using System.Windows.Interop;
using Thalamus.Core.Models;
using Thalamus.Interop;

namespace Thalamus.Services;

internal sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0x5448;
    private readonly HwndSource _source;
    private readonly Action _action;
    private bool _registered;
    private bool _disposed;

    internal HotkeyService(HotkeySettings settings, Action action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(action);
        _action = action;
        var parameters = new HwndSourceParameters("Thalamus.Hotkey")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            ExtendedWindowStyle = unchecked((int)NativeMethods.WS_EX_NOACTIVATE),
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var modifiers = NativeMethods.MOD_NOREPEAT;
        if (settings.Control) modifiers |= NativeMethods.MOD_CONTROL;
        if (settings.Alt) modifiers |= NativeMethods.MOD_ALT;
        if (settings.Shift) modifiers |= NativeMethods.MOD_SHIFT;
        if (settings.Windows) modifiers |= NativeMethods.MOD_WIN;

        _registered = NativeMethods.RegisterHotKey(
            _source.Handle, HotkeyId, modifiers, (uint)settings.VirtualKey);
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _action();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
