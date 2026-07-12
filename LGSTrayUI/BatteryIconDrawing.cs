using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayPrimitives;
using LGSTrayUI.Properties;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LGSTrayUI;

public static partial class BatteryIconDrawing
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    private static Bitmap Mouse => CheckTheme.TaskbarLightTheme ? Resources.Mouse : Resources.Mouse_dark;
    private static Bitmap Keyboard => CheckTheme.TaskbarLightTheme ? Resources.Keyboard : Resources.Keyboard_dark;
    private static Bitmap Headset => CheckTheme.TaskbarLightTheme ? Resources.Headset : Resources.Headset_dark;
    private static Bitmap Battery => CheckTheme.TaskbarLightTheme ? Resources.Battery : Resources.Battery_dark;
    private static Bitmap Missing => CheckTheme.TaskbarLightTheme ? Resources.Missing : Resources.Missing_dark;
    private static Bitmap Charging => CheckTheme.TaskbarLightTheme ? Resources.Charging : Resources.Charging_dark;

    private static readonly int ImageSize;

    static BatteryIconDrawing()
    {
        int dpi;
        try
        {
            using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\ThemeManager",
                false
            );
            string? configuredDpi = registry?.GetValue("LastLoadedDPI") as string;
            if (!int.TryParse(configuredDpi, out dpi))
            {
                dpi = 96;
            }
        }
        catch
        {
            dpi = 96;
        }

        ImageSize = Math.Max(16, (int)Math.Round(32 * (dpi / 96f)));
    }

    public static void DrawUnknown(TaskbarIcon taskbarIcon)
    {
        DrawIcon(taskbarIcon, new LogiDevice { BatteryPercentage = -1 });
    }

    public static void DrawIcon(TaskbarIcon taskbarIcon, LogiDevice device)
    {
        Rectangle destination = new(0, 0, ImageSize, ImageSize);
        using Bitmap bitmap = new(ImageSize, ImageSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics, SmoothingMode.HighQuality);
        using ImageAttributes wrapMode = new();
        wrapMode.SetWrapMode(WrapMode.TileFlipXY);

        Bitmap[] layers = [GetBatteryValue(device), Battery, GetDeviceIcon(device)];
        foreach (Bitmap image in layers)
        {
            graphics.DrawImage(
                image,
                destination,
                0,
                0,
                image.Width,
                image.Height,
                GraphicsUnit.Pixel,
                wrapMode
            );
        }

        ReplaceIcon(taskbarIcon, CreateManagedIcon(bitmap));
    }

    public static void DrawNumeric(TaskbarIcon taskbarIcon, LogiDevice device)
    {
        using Bitmap bitmap = new(ImageSize, ImageSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics, SmoothingMode.HighQuality);

        string displayString = device.BatteryPercentage < 0 ? "?" : $"{device.BatteryPercentage:f0}";
        using Font font = new("Segoe UI", (int)(0.8 * ImageSize), GraphicsUnit.Pixel);
        using SolidBrush brush = new(GetDeviceColor(device));
        using StringFormat format = new(StringFormatFlags.FitBlackBox)
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center,
        };
        graphics.DrawString(displayString, font, brush, ImageSize / 2f, ImageSize / 2f, format);

        ReplaceIcon(taskbarIcon, CreateManagedIcon(bitmap));
    }

    public static void DrawAlert(TaskbarIcon taskbarIcon, LogiDevice device)
    {
        Rectangle destination = new(0, 0, ImageSize, ImageSize);
        using Bitmap bitmap = new(ImageSize, ImageSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics, SmoothingMode.AntiAlias);
        using ImageAttributes wrapMode = new();
        wrapMode.SetWrapMode(WrapMode.TileFlipXY);

        Bitmap deviceIcon = GetDeviceIcon(device);
        graphics.DrawImage(
            deviceIcon,
            destination,
            0,
            0,
            deviceIcon.Width,
            deviceIcon.Height,
            GraphicsUnit.Pixel,
            wrapMode
        );

        using Pen pen = new(Color.FromArgb(0xE8, 0x11, 0x23), Math.Max(2, ImageSize / 12));
        graphics.DrawEllipse(pen, 2, 2, ImageSize - 4, ImageSize - 4);
        using Font font = new("Segoe UI", (int)(0.68 * ImageSize), FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush brush = new(Color.FromArgb(0xE8, 0x11, 0x23));
        using StringFormat format = new(StringFormatFlags.FitBlackBox)
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center,
        };
        graphics.DrawString("!", font, brush, ImageSize / 2f, ImageSize / 2f, format);

        ReplaceIcon(taskbarIcon, CreateManagedIcon(bitmap));
    }

    private static void ConfigureGraphics(Graphics graphics, SmoothingMode smoothingMode)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = smoothingMode;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static Icon CreateManagedIcon(Bitmap bitmap)
    {
        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(iconHandle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    private static void ReplaceIcon(TaskbarIcon taskbarIcon, Icon replacement)
    {
        Icon? previous = taskbarIcon.Icon;
        taskbarIcon.Icon = replacement;
        if (!ReferenceEquals(previous, replacement))
        {
            previous?.Dispose();
        }
    }

    private static Bitmap GetDeviceIcon(LogiDevice device) => device.DeviceType switch
    {
        DeviceType.Keyboard => Keyboard,
        DeviceType.Headset => Headset,
        _ => Mouse,
    };

    private static Color GetDeviceColor(LogiDevice device) =>
        CheckTheme.TaskbarLightTheme ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(0xEE, 0xEE, 0xEE);

    private static Bitmap GetBatteryValue(LogiDevice device) => device.BatteryPercentage switch
    {
        _ when device.PowerSupplyStatus == PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING => Charging,
        < 0 => Missing,
        < 10 => Resources.Indicator_10,
        < 50 => Resources.Indicator_30,
        < 85 => Resources.Indicator_50,
        _ => Resources.Indicator_100,
    };
}
