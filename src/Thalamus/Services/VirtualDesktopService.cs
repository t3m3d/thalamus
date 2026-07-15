using System.Runtime.InteropServices;
using Thalamus.Core.Commands;
using Thalamus.Core.Services;

namespace Thalamus.Services;

internal sealed class VirtualDesktopService : IVirtualDesktopService, IDisposable
{
    private static readonly Guid ManagerClassId = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    public VirtualDesktopService()
    {
        object? instance = null;
        var available = false;
        try
        {
            var type = Type.GetTypeFromCLSID(ManagerClassId, throwOnError: false);
            instance = type is null ? null : Activator.CreateInstance(type);
            available = instance is IVirtualDesktopManagerNative;
        }
        catch
        {
            available = false;
        }
        finally
        {
            ReleaseComObjectSafely(instance);
        }

        Capabilities = !available
            ? new(false, false, false, "The documented Windows virtual desktop manager is unavailable.")
            : new(
                true,
                true,
                false,
                "Windows exposes membership queries and moves to a known desktop ID, but no documented API for adjacent desktop enumeration or switching.");
    }

    public VirtualDesktopCapabilities Capabilities { get; }

    public Task<bool> SwitchAsync(Direction direction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<bool> MoveWindowAsync(long handle, Direction direction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private static void ReleaseComObjectSafely(object? instance)
    {
        if (instance is null)
            return;

        try
        {
            if (Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
            // Capability probing is optional; COM teardown cannot be allowed to
            // turn an unsupported or damaged desktop service into an app failure.
        }
    }

    public void Dispose()
    {
        // Capability detection releases its COM proxy on the creating apartment.
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerNative
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(
            IntPtr topLevelWindow,
            [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, in Guid desktopId);
    }
}
