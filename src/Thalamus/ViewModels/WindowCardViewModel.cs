using System.Windows.Media;
using Thalamus.Core.Models;

namespace Thalamus.ViewModels;

internal sealed class WindowCardViewModel(
    WindowSnapshot window,
    string groupLabel,
    ImageSource? icon)
{
    internal WindowSnapshot Window { get; } = window;
    public long Handle => Window.Handle;
    public string Title => Window.Title;
    public string ApplicationId => Window.ApplicationId;
    public string GroupLabel { get; } = groupLabel;
    public string MonitorDeviceName => Window.MonitorDeviceName;
    public bool IsMinimized => Window.IsMinimized;
    public ImageSource? Icon { get; } = icon;
}
