using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayUI;

public sealed partial class SystemStateService
{
    public bool IsGHubRunning()
    {
        return Process.GetProcessesByName("lghub").Length > 0 ||
               Process.GetProcessesByName("lghub_agent").Length > 0 ||
               Process.GetProcessesByName("lghub_system_tray").Length > 0;
    }

    public async Task<bool> IsPort9010ReachableAsync()
    {
        try
        {
            using TcpClient client = new();
            await client.ConnectAsync("localhost", 9010).WaitAsync(TimeSpan.FromMilliseconds(700));
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public bool IsForegroundFullscreen()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out RECT rect))
        {
            return false;
        }

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo = new()
        {
            cbSize = Marshal.SizeOf<MONITORINFO>(),
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        Rect window = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        Rect screen = new(
            monitorInfo.rcMonitor.Left,
            monitorInfo.rcMonitor.Top,
            monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
            monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
        );

        return window.Width >= screen.Width - 2 &&
               window.Height >= screen.Height - 2 &&
               window.Left <= screen.Left + 2 &&
               window.Top <= screen.Top + 2;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
