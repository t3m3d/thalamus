using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Thalamus.Interop;

namespace Thalamus.Services;

internal sealed class DwmThumbnailService : IThumbnailService
{
    private readonly Window _window;
    private readonly IntPtr _destination;
    private readonly Dictionary<FrameworkElement, ThumbnailEntry> _thumbnails = [];
    private bool _updateScheduled;
    private bool _disposed;

    private sealed class ThumbnailEntry(IntPtr thumbnail)
    {
        internal IntPtr Thumbnail { get; } = thumbnail;
        internal (int Left, int Top, int Right, int Bottom)? Destination { get; set; }
        internal bool Visible { get; set; }
    }

    internal DwmThumbnailService(Window window)
    {
        _window = window;
        _destination = new WindowInteropHelper(window).EnsureHandle();
        _window.SizeChanged += OnWindowLayoutChanged;
        _window.LocationChanged += OnWindowLayoutChanged;
        _window.LayoutUpdated += OnWindowLayoutChanged;
    }

    public bool Register(FrameworkElement previewHost, long sourceHandle)
    {
        if (_disposed)
            return false;

        Unregister(previewHost);
        var source = new IntPtr(sourceHandle);
        if (!NativeMethods.IsWindow(source) ||
            NativeMethods.DwmRegisterThumbnail(_destination, source, out var thumbnail) != 0)
            return false;

        _thumbnails[previewHost] = new ThumbnailEntry(thumbnail);
        if (previewHost.Dispatcher.HasShutdownStarted ||
            previewHost.Dispatcher.HasShutdownFinished)
        {
            Unregister(previewHost);
            return false;
        }

        try
        {
            previewHost.Dispatcher.BeginInvoke(() => Update(previewHost));
        }
        catch (InvalidOperationException)
        {
            Unregister(previewHost);
            return false;
        }

        return true;
    }

    public void Update(FrameworkElement previewHost)
    {
        if (_disposed)
            return;

        if (!_thumbnails.TryGetValue(previewHost, out var entry))
            return;

        if (!previewHost.IsVisible ||
            previewHost.ActualWidth <= 1 ||
            previewHost.ActualHeight <= 1)
        {
            Hide(previewHost, entry);
            return;
        }

        try
        {
            var source = PresentationSource.FromVisual(_window);
            if (source?.CompositionTarget is null)
            {
                Hide(previewHost, entry);
                return;
            }

            var transform = source.CompositionTarget.TransformToDevice;
            var originDip = previewHost.TranslatePoint(new Point(0, 0), _window);
            var origin = transform.Transform(originDip);
            var extent = transform.Transform(
                new Point(previewHost.ActualWidth, previewHost.ActualHeight));
            var hostBounds = (
                Left: (int)Math.Round(origin.X),
                Top: (int)Math.Round(origin.Y),
                Right: (int)Math.Round(origin.X + extent.X),
                Bottom: (int)Math.Round(origin.Y + extent.Y));

            if (!IsFullyInsideViewport(previewHost, hostBounds, transform) ||
                NativeMethods.DwmQueryThumbnailSourceSize(entry.Thumbnail, out var sourceSize) != 0 ||
                sourceSize.Width <= 0 ||
                sourceSize.Height <= 0)
            {
                Hide(previewHost, entry);
                return;
            }

            var width = Math.Max(1, hostBounds.Right - hostBounds.Left);
            var height = Math.Max(1, hostBounds.Bottom - hostBounds.Top);
            var scale = Math.Min(width / (double)sourceSize.Width, height / (double)sourceSize.Height);
            var fittedWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            var fittedHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            var left = hostBounds.Left + (width - fittedWidth) / 2;
            var top = hostBounds.Top + (height - fittedHeight) / 2;
            var destination = (
                Left: left,
                Top: top,
                Right: left + fittedWidth,
                Bottom: top + fittedHeight);
            if (entry.Visible && entry.Destination == destination)
                return;

            var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                Flags = NativeMethods.DWM_TNP_RECTDESTINATION |
                    NativeMethods.DWM_TNP_VISIBLE |
                    NativeMethods.DWM_TNP_OPACITY |
                    NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
                Destination = new NativeMethods.RECT
                {
                    Left = destination.Left,
                    Top = destination.Top,
                    Right = destination.Right,
                    Bottom = destination.Bottom
                },
                Opacity = 255,
                Visible = true,
                SourceClientAreaOnly = false
            };
            if (NativeMethods.DwmUpdateThumbnailProperties(entry.Thumbnail, ref properties) == 0)
            {
                entry.Destination = destination;
                entry.Visible = true;
            }
            else
            {
                Unregister(previewHost);
            }
        }
        catch (InvalidOperationException)
        {
            // The host left the visual tree during an event burst.
            Hide(previewHost, entry);
        }
    }

    public void Unregister(FrameworkElement previewHost)
    {
        if (_thumbnails.Remove(previewHost, out var entry))
            _ = NativeMethods.DwmUnregisterThumbnail(entry.Thumbnail);
    }

    private bool IsFullyInsideViewport(
        FrameworkElement previewHost,
        (int Left, int Top, int Right, int Bottom) hostBounds,
        Matrix transform)
    {
        const int tolerance = 1;
        var windowExtent = transform.Transform(new Point(_window.ActualWidth, _window.ActualHeight));
        if (hostBounds.Left < -tolerance ||
            hostBounds.Top < -tolerance ||
            hostBounds.Right > Math.Round(windowExtent.X) + tolerance ||
            hostBounds.Bottom > Math.Round(windowExtent.Y) + tolerance)
            return false;

        var viewport = FindAncestor<ScrollViewer>(previewHost);
        if (viewport is null)
            return true;

        var viewportOrigin = transform.Transform(
            viewport.TranslatePoint(new Point(0, 0), _window));
        var viewportExtent = transform.Transform(
            new Point(viewport.ActualWidth, viewport.ActualHeight));
        return hostBounds.Left >= Math.Floor(viewportOrigin.X) - tolerance &&
            hostBounds.Top >= Math.Floor(viewportOrigin.Y) - tolerance &&
            hostBounds.Right <= Math.Ceiling(viewportOrigin.X + viewportExtent.X) + tolerance &&
            hostBounds.Bottom <= Math.Ceiling(viewportOrigin.Y + viewportExtent.Y) + tolerance;
    }

    private void Hide(FrameworkElement previewHost, ThumbnailEntry entry)
    {
        entry.Destination = null;
        if (!entry.Visible)
            return;

        var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            Flags = NativeMethods.DWM_TNP_VISIBLE,
            Visible = false
        };
        if (NativeMethods.DwmUpdateThumbnailProperties(entry.Thumbnail, ref properties) == 0)
            entry.Visible = false;
        else
            Unregister(previewHost);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e)
    {
        if (_disposed || _updateScheduled)
            return;

        _updateScheduled = true;
        try
        {
            _window.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(ProcessScheduledUpdate));
        }
        catch (InvalidOperationException)
        {
            _updateScheduled = false;
        }
    }

    private void ProcessScheduledUpdate()
    {
        _updateScheduled = false;
        if (_disposed)
            return;

        foreach (var host in _thumbnails.Keys.ToArray())
            Update(host);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SizeChanged -= OnWindowLayoutChanged;
        _window.LocationChanged -= OnWindowLayoutChanged;
        _window.LayoutUpdated -= OnWindowLayoutChanged;
        foreach (var entry in _thumbnails.Values)
            _ = NativeMethods.DwmUnregisterThumbnail(entry.Thumbnail);
        _thumbnails.Clear();
    }
}
