using System.Windows;

namespace Thalamus.Services;

internal interface IHotkeyService : IDisposable
{
    bool IsRegistered { get; }
}

internal interface IThumbnailService : IDisposable
{
    bool Register(FrameworkElement previewHost, long sourceHandle);
    void Update(FrameworkElement previewHost);
    void Unregister(FrameworkElement previewHost);
}
