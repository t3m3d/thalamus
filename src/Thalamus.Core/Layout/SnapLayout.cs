using Thalamus.Core.Commands;
using Thalamus.Core.Models;

namespace Thalamus.Core.Layout;

public static class SnapLayout
{
    public static RectI Calculate(RectI workArea, TileTarget target)
    {
        if (!workArea.IsValid)
            throw new ArgumentException("A valid work area is required.", nameof(workArea));

        var halfWidth = workArea.Width / 2;
        var halfHeight = workArea.Height / 2;
        var thirdWidth = workArea.Width / 3;
        return target switch
        {
            TileTarget.Left => new(workArea.X, workArea.Y, halfWidth, workArea.Height),
            TileTarget.Right => new(workArea.X + halfWidth, workArea.Y, workArea.Width - halfWidth, workArea.Height),
            TileTarget.Top => new(workArea.X, workArea.Y, workArea.Width, halfHeight),
            TileTarget.Bottom => new(workArea.X, workArea.Y + halfHeight, workArea.Width, workArea.Height - halfHeight),
            TileTarget.LeftThird => new(workArea.X, workArea.Y, thirdWidth, workArea.Height),
            TileTarget.CenterThird => new(workArea.X + thirdWidth, workArea.Y, thirdWidth, workArea.Height),
            TileTarget.RightThird => new(workArea.X + thirdWidth * 2, workArea.Y, workArea.Width - thirdWidth * 2, workArea.Height),
            TileTarget.TopLeft => new(workArea.X, workArea.Y, halfWidth, halfHeight),
            TileTarget.TopRight => new(workArea.X + halfWidth, workArea.Y, workArea.Width - halfWidth, halfHeight),
            TileTarget.BottomLeft => new(workArea.X, workArea.Y + halfHeight, halfWidth, workArea.Height - halfHeight),
            TileTarget.BottomRight => new(workArea.X + halfWidth, workArea.Y + halfHeight, workArea.Width - halfWidth, workArea.Height - halfHeight),
            TileTarget.Maximize => workArea,
            TileTarget.Restore => workArea,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }
}
