namespace Thalamus.Core.Models;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsValid => Width > 0 && Height > 0;
    public int Area => IsValid ? Width * Height : 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public RectI Intersect(RectI other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top
            ? new RectI(left, top, right - left, bottom - top)
            : default;
    }
}

public sealed record MonitorSnapshot(
    string DeviceName,
    RectI Bounds,
    RectI WorkArea,
    uint DpiX = 96,
    uint DpiY = 96,
    bool IsPrimary = false)
{
    public double ScaleX => DpiX / 96d;
    public double ScaleY => DpiY / 96d;
}
