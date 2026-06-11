using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace LGSTrayUI;

internal static class TrayContextMenuPlacement
{
    public static void Attach(TaskbarIcon taskbarIcon)
    {
        taskbarIcon.PreviewTrayContextMenuOpen += OnPreviewTrayContextMenuOpen;
    }

    public static void Detach(TaskbarIcon taskbarIcon)
    {
        taskbarIcon.PreviewTrayContextMenuOpen -= OnPreviewTrayContextMenuOpen;
    }

    private static void OnPreviewTrayContextMenuOpen(object sender, RoutedEventArgs e)
    {
        if (sender is not TaskbarIcon taskbarIcon || taskbarIcon.ContextMenu is not { } menu)
        {
            return;
        }

        e.Handled = true;
        SetMenuDeviceContext(taskbarIcon, menu);

        if (!taskbarIcon.Dispatcher.CheckAccess())
        {
            _ = taskbarIcon.Dispatcher.BeginInvoke(() => OpenAtCursor(menu));
            return;
        }

        OpenAtCursor(menu);
    }

    private static void SetMenuDeviceContext(TaskbarIcon taskbarIcon, ContextMenu menu)
    {
        if (menu.DataContext is NotifyIconViewModel viewModel)
        {
            viewModel.SetMenuDeviceContext(taskbarIcon.DataContext as LogiDeviceViewModel);
        }
    }

    private static void OpenAtCursor(ContextMenu menu)
    {
        if (menu.IsOpen)
        {
            menu.IsOpen = false;
        }

        CursorPlacementMetrics metrics = GetCursorPlacementMetrics();
        Size menuSize = MeasureMenu(menu);
        Point location = CalculateMenuLocation(metrics.Cursor, metrics.WorkArea, menuSize);

        menu.PlacementTarget = null;
        menu.Placement = PlacementMode.Absolute;
        menu.HorizontalOffset = location.X;
        menu.VerticalOffset = location.Y;
        menu.IsOpen = true;

        SetForegroundWindow(menu);
    }

    private static Size MeasureMenu(ContextMenu menu)
    {
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double width = Math.Max(menu.DesiredSize.Width, menu.ActualWidth);
        double height = Math.Max(menu.DesiredSize.Height, menu.ActualHeight);

        if (width <= 0)
        {
            width = 220;
        }

        if (height <= 0)
        {
            height = 280;
        }

        return new Size(width, height);
    }

    private static Point CalculateMenuLocation(Point cursor, Rect workArea, Size menuSize)
    {
        const double gap = 2;

        double anchorX = Clamp(cursor.X, workArea.Left, workArea.Right);
        double anchorY = Clamp(cursor.Y, workArea.Top, workArea.Bottom);

        double maxLeft = Math.Max(workArea.Left, workArea.Right - menuSize.Width);
        double left = anchorX + gap;
        if (left + menuSize.Width > workArea.Right)
        {
            left = anchorX - menuSize.Width - gap;
        }
        left = Clamp(left, workArea.Left, maxLeft);

        double maxTop = Math.Max(workArea.Top, workArea.Bottom - menuSize.Height);
        double top = anchorY - menuSize.Height - gap;
        if (top < workArea.Top && anchorY + menuSize.Height + gap <= workArea.Bottom)
        {
            top = anchorY + gap;
        }
        top = Clamp(top, workArea.Top, maxTop);

        return new Point(left, top);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        if (value > maximum)
        {
            return maximum;
        }

        return value;
    }

    private static CursorPlacementMetrics GetCursorPlacementMetrics()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return new CursorPlacementMetrics(new Point(0, 0), SystemParameters.WorkArea);
        }

        double dpiX = 96;
        double dpiY = 96;
        NativeMethods.RECT workArea = default;
        bool hasWorkArea = false;

        try
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint x, out uint y) == 0 &&
                    x > 0 &&
                    y > 0)
                {
                    dpiX = x;
                    dpiY = y;
                }

                NativeMethods.MONITORINFO monitorInfo = new()
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>(),
                };
                if (NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
                {
                    workArea = monitorInfo.rcWork;
                    hasWorkArea = true;
                }
            }
        }
        catch
        {
            dpiX = 96;
            dpiY = 96;
            hasWorkArea = false;
        }

        double scaleX = 96.0 / dpiX;
        double scaleY = 96.0 / dpiY;
        Point cursorInDips = new(cursor.X * scaleX, cursor.Y * scaleY);
        Rect workAreaInDips = hasWorkArea
            ? new Rect(
                workArea.Left * scaleX,
                workArea.Top * scaleY,
                (workArea.Right - workArea.Left) * scaleX,
                (workArea.Bottom - workArea.Top) * scaleY)
            : SystemParameters.WorkArea;

        return new CursorPlacementMetrics(cursorInDips, workAreaInDips);
    }

    private static void SetForegroundWindow(ContextMenu menu)
    {
        try
        {
            if (PresentationSource.FromVisual(menu) is HwndSource { Handle: { } handle } &&
                handle != IntPtr.Zero)
            {
                _ = NativeMethods.SetForegroundWindow(handle);
                return;
            }

            IntPtr mainHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (mainHandle != IntPtr.Zero)
            {
                _ = NativeMethods.SetForegroundWindow(mainHandle);
            }
        }
        catch
        {
            // ContextMenu still works without foreground promotion; this only improves click-away closing.
        }
    }

    private static class NativeMethods
    {
        internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        internal const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }

    private readonly record struct CursorPlacementMetrics(Point Cursor, Rect WorkArea);
}
