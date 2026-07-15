using System.Diagnostics;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Thalamus.Interop;
using Thalamus.Core.Models;

namespace Thalamus.Services;

internal static class IconService
{
    private const int MaximumEntries = 256;
    private const int TrimCount = 64;
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(Lazy<ImageSource?> Icon, long CreatedAt);

    internal static ImageSource? GetIcon(WindowSnapshot window)
    {
        var key = $"{window.ApplicationId}|{window.ClassName}|{window.ProcessId}";
        if (Cache.Count >= MaximumEntries && !Cache.ContainsKey(key))
        {
            foreach (var staleKey in Cache.Keys.Take(TrimCount))
                Cache.TryRemove(staleKey, out _);
        }

        var entry = Cache.GetOrAdd(
            key,
            _ => new CacheEntry(
                new Lazy<ImageSource?>(
                    () => Resolve(window.Handle),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                Stopwatch.GetTimestamp()));
        var icon = entry.Icon.Value;
        if (icon is null && Stopwatch.GetElapsedTime(entry.CreatedAt) >= NegativeCacheLifetime)
            Cache.TryRemove(key, out _);
        return icon;
    }

    private static ImageSource? Resolve(long handle)
    {
        var hWnd = new IntPtr(handle);
        if (!NativeMethods.IsWindow(hWnd))
            return null;

        var icon = GetMessageIcon(hWnd, NativeMethods.ICON_BIG)
            ?? GetMessageIcon(hWnd, NativeMethods.ICON_SMALL2)
            ?? GetMessageIcon(hWnd, NativeMethods.ICON_SMALL);

        var iconHandle = icon.GetValueOrDefault();
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCLP_HICON);
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCLP_HICONSM);
        if (iconHandle == IntPtr.Zero)
            return null;

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr? GetMessageIcon(IntPtr hWnd, int size)
    {
        var sent = NativeMethods.SendMessageTimeoutW(
            hWnd,
            NativeMethods.WM_GETICON,
            new IntPtr(size),
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            50,
            out var result);
        return sent != IntPtr.Zero && result != IntPtr.Zero ? result : null;
    }
}
