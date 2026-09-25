using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LGSTrayUI;

internal static class SettingsWindowPlacement
{
    internal static void Apply(Window window, Size? preferredSize = null, Size? preferredMinimum = null)
    {
        if (window.WindowState != WindowState.Normal) return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out NativeRect current)) return;
        MonitorInfo monitor = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;

        double scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
        Rect workArea = monitor.Work.ToRect();
        if (workArea.Width <= 0 || workArea.Height <= 0) return;
        window.MinWidth = Math.Min(preferredMinimum?.Width ?? window.MinWidth, workArea.Width / scale);
        window.MinHeight = Math.Min(preferredMinimum?.Height ?? window.MinHeight, workArea.Height / scale);

        Size desired = preferredSize is { } size
            ? new Size(size.Width * scale, size.Height * scale)
            : current.ToRect().Size;
        Rect fitted = WindowBoundsPolicy.Fit(current.ToRect(), desired, workArea);
        _ = SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(fitted.Left),
            (int)Math.Round(fitted.Top), (int)Math.Floor(fitted.Width),
            (int)Math.Floor(fitted.Height), 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
