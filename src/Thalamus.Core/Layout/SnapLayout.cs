using Thalamus.Core.Commands;
using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class SnapLayout
{
    public static RectI Calculate(RectI workArea, TileTarget target)
    {
        if (!workArea.IsValid)
            throw new ArgumentException("A valid work area is required.", nameof(workArea));

        var halfX = workArea.Width / 2;
        var halfY = workArea.Height / 2;
        var firstThirdX = workArea.Width / 3;
        var secondThirdX = (int)((long)workArea.Width * 2 / 3);
        var leftHalfWidth = Math.Max(1, halfX);
        var rightHalfWidth = Math.Max(1, workArea.Width - halfX);
        var topHalfHeight = Math.Max(1, halfY);
        var bottomHalfHeight = Math.Max(1, workArea.Height - halfY);
        var leftThirdWidth = Math.Max(1, firstThirdX);
        var centerThirdWidth = Math.Max(1, secondThirdX - firstThirdX);
        var rightThirdWidth = Math.Max(1, workArea.Width - secondThirdX);
        return target switch
        {
            TileTarget.Left => new(workArea.X, workArea.Y, leftHalfWidth, workArea.Height),
            TileTarget.Right => new(workArea.X + halfX, workArea.Y, rightHalfWidth, workArea.Height),
            TileTarget.Top => new(workArea.X, workArea.Y, workArea.Width, topHalfHeight),
            TileTarget.Bottom => new(workArea.X, workArea.Y + halfY, workArea.Width, bottomHalfHeight),
            TileTarget.LeftThird => new(workArea.X, workArea.Y, leftThirdWidth, workArea.Height),
            TileTarget.CenterThird => new(workArea.X + firstThirdX, workArea.Y, centerThirdWidth, workArea.Height),
            TileTarget.RightThird => new(workArea.X + secondThirdX, workArea.Y, rightThirdWidth, workArea.Height),
            TileTarget.TopLeft => new(workArea.X, workArea.Y, leftHalfWidth, topHalfHeight),
            TileTarget.TopRight => new(workArea.X + halfX, workArea.Y, rightHalfWidth, topHalfHeight),
            TileTarget.BottomLeft => new(workArea.X, workArea.Y + halfY, leftHalfWidth, bottomHalfHeight),
            TileTarget.BottomRight => new(workArea.X + halfX, workArea.Y + halfY, rightHalfWidth, bottomHalfHeight),
            TileTarget.Maximize => workArea,
            TileTarget.Restore => workArea,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }
}
