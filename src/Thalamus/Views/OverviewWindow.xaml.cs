using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using Thalamus.Core.Commands;
using Thalamus.Core.Models;
using Thalamus.Services;
using Thalamus.Interop;
using Thalamus.ViewModels;

namespace Thalamus.Views;

public partial class OverviewWindow : Window
{
    private readonly Func<Window, IThumbnailService> _thumbnailFactory;
    private IThumbnailService? _thumbnails;
    private Point _dragStart;
    private FrameworkElement? _dragCard;
    private WindowCardViewModel? _draggedWindow;
    private bool _isDragging;
    private readonly ThalamusSettings _settings;

    internal OverviewWindow(
        OverviewViewModel viewModel,
        ThalamusSettings settings,
        Func<Window, IThumbnailService>? thumbnailFactory = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _thumbnailFactory = thumbnailFactory ?? (window => new DwmThumbnailService(window));
        ApplyAppearance();
    }
    private void ApplyAppearance()
    {
        ThemeService.Apply(this, _settings);
        if (!SystemParameters.HighContrast)
            return;

        Background = SystemColors.WindowBrush;
        Foreground = SystemColors.WindowTextBrush;
        Resources["SurfaceBrush"] = SystemColors.WindowBrush;
        Resources["SurfaceHoverBrush"] = SystemColors.HighlightBrush;
        Resources["PreviewBackgroundBrush"] = SystemColors.WindowBrush;
        Resources["BorderBrush"] = SystemColors.WindowTextBrush;
        Resources["AccentBrush"] = SystemColors.HighlightBrush;
        Resources["PrimaryTextBrush"] = SystemColors.WindowTextBrush;
        Resources["SecondaryTextBrush"] = Resources["MutedTextBrush"] = SystemColors.GrayTextBrush;
        Resources["ToolbarBackgroundBrush"] = SystemColors.WindowBrush;
        Resources["ToolbarHoverBrush"] = SystemColors.HighlightBrush;
        Resources["ToolbarHoverTextBrush"] = SystemColors.HighlightTextBrush;
        Resources["SelectionHoverBrush"] = SystemColors.HighlightBrush;
        Resources["SelectionBackgroundBrush"] = SystemColors.HighlightBrush;
        Resources["IconBackgroundBrush"] = SystemColors.WindowBrush;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            !string.Equals(
                e.PropertyName,
                nameof(SystemParameters.HighContrast),
                StringComparison.Ordinal))
            return;

        if (Dispatcher.CheckAccess())
        {
            ApplyAppearance();
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(new Action(ApplyAppearance));
        }
        catch (InvalidOperationException)
        {
            // The accessibility change raced window shutdown.
        }
    }

    private OverviewViewModel ViewModel => (OverviewViewModel)DataContext;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_thumbnails is not null)
            return;

        FitToVirtualScreen();
        _thumbnails = _thumbnailFactory(this);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Activate();
        WindowList.Focus();
        if (!_settings.ReducedMotion && !SystemParameters.HighContrast)
            StartEntryAnimation();
    }

    private void StartEntryAnimation()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = new ScaleTransform(0.985, 0.985);
        RootGrid.Opacity = 0.88;
        RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        RootGrid.RenderTransform = scale;
        RootGrid.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, duration) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, duration) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, duration) { EasingFunction = easing });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                if (Keyboard.FocusedElement is Button)
                    return;

                ViewModel.ActivateSelected();
                e.Handled = true;
                break;
            case Key.Left when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ViewModel.MoveSelectedToAdjacentMonitor(-1);
                e.Handled = true;
                break;
            case Key.Right when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ViewModel.MoveSelectedToAdjacentMonitor(1);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.D1 when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                ViewModel.TileSelected(TileTarget.Left);
                e.Handled = true;
                break;
            case Key.D2 when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                ViewModel.TileSelected(TileTarget.Right);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        ViewModel.MoveSelection(delta);
        if (ViewModel.SelectedWindow is { } selected)
            WindowList.ScrollIntoView(selected);
    }


    private void PreviewHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host && host.DataContext is WindowCardViewModel card)
            _thumbnails?.Register(host, card.Handle);
    }

    private void PreviewHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host)
            _thumbnails?.Unregister(host);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement host)
            _thumbnails?.Update(host);
    }


    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WindowCardViewModel card } element)
        {
            ResetDrag();
            return;
        }

        ResetDrag();
        if (e.OriginalSource is DependencyObject source &&
            FindAncestor<Button>(source) is not null)
            return;

        if (e.ClickCount == 2)
        {
            ViewModel.ActivateCommand.Execute(card);
            e.Handled = true;
            return;
        }

        _isDragging = false;
        _dragStart = e.GetPosition(this);
        _dragCard = element;
        _draggedWindow = card;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragCard) ||
            e.LeftButton != MouseButtonState.Pressed || _draggedWindow is null)
            return;

        var current = e.GetPosition(this);
        if (!_isDragging &&
            Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDragging)
            return;

        _isDragging = Mouse.Capture(_dragCard);
        if (!_isDragging)
            return;

        _dragCard.Opacity = 0.72;
        ViewModel.Status = "Drop on another monitor to move the window";
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCard is null || _draggedWindow is null)
            return;

        if (!ReferenceEquals(sender, _dragCard))
        {
            ResetDrag();
            return;
        }

        if (!_isDragging)
        {
            ResetDrag();
            return;
        }

        var screenPoint = PointToScreen(e.GetPosition(this));
        var target = ViewModel.Monitors.FirstOrDefault(m =>
            m.Bounds.Contains((int)screenPoint.X, (int)screenPoint.Y));
        if (target is not null &&
            !string.Equals(target.DeviceName, _draggedWindow.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.MoveToMonitor(_draggedWindow, target.DeviceName);
        }
        else
        {
            ViewModel.Status = "Window remains on its current monitor";
        }

        ResetDrag();
        e.Handled = true;
    }


    private void Card_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging && ReferenceEquals(sender, _dragCard))
            ResetDrag();
    }
    private void ResetDrag()
    {
        if (_dragCard is not null)
            _dragCard.Opacity = 1;

        var captured = _dragCard;
        _dragCard = null;
        _draggedWindow = null;
        _isDragging = false;
        if (captured is not null && Mouse.Captured == captured)
            Mouse.Capture(null);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }


    private void FitToVirtualScreen()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && width > 0 && height > 0 &&
            NativeMethods.SetWindowPos(
                handle, IntPtr.Zero, x, y, width, height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
            return;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            Dispatcher.BeginInvoke(new Action(FitToVirtualScreen));
        }
        catch (InvalidOperationException)
        {
            // The display event raced the window or application shutdown.
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            Dispatcher.BeginInvoke(new Action(FitToVirtualScreen));
        }
        catch (InvalidOperationException)
        {
            // The DPI transition raced the window or application shutdown.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ResetDrag();
        _thumbnails?.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _thumbnails = null;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnClosed(e);
    }
}
