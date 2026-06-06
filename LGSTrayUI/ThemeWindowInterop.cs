using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace LGSTrayUI;

public static partial class ThemeWindowInterop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int enabled = CheckTheme.LightTheme ? 0 : 1;
        int attribute = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985)
            ? DwmwaUseImmersiveDarkMode
            : DwmwaUseImmersiveDarkModeBefore20h1;

        _ = DwmSetWindowAttribute(hwnd, attribute, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
