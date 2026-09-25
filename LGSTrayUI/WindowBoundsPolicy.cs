using System;
using System.Windows;

namespace LGSTrayUI;

internal static class WindowBoundsPolicy
{
    // All coordinates are physical pixels, including monitors with negative origins.
    internal static Rect Fit(Rect current, Size desired, Rect workArea)
    {
        double width = Math.Min(Math.Max(1, desired.Width), workArea.Width);
        double height = Math.Min(Math.Max(1, desired.Height), workArea.Height);
        return new Rect(
            Math.Clamp(current.Left, workArea.Left, workArea.Right - width),
            Math.Clamp(current.Top, workArea.Top, workArea.Bottom - height),
            width, height);
    }
}
