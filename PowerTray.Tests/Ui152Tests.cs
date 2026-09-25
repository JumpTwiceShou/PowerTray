using LGSTrayCore;
using LGSTrayPrimitives;
using LGSTrayUI;
using System.Drawing;
using System.Windows;

internal static class Ui152Tests
{
    internal static void Run()
    {
        TestQuietTimeValidation();
        TestWindowFitting();
        TestNumericGlyphBounds();
        TestDeviceSettingsScopes();
        TestLocalizedLabels();
        Console.WriteLine("PowerTray 1.5.2 UI behavior tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestQuietTimeValidation()
    {
        Check(TimeOfDayInput.TryNormalize("9:05", out string morning) && morning == "09:05", "Short hour should normalize.");
        Check(TimeOfDayInput.TryNormalize("23:59", out string late) && late == "23:59", "Late time should remain valid.");
        foreach (string invalid in new[] { "", "25:00", "12:60", "-1:00", "12:30:45" })
            Check(!TimeOfDayInput.TryNormalize(invalid, out _), $"Invalid time {invalid} must not be stored.");
    }

    private static void TestWindowFitting()
    {
        Rect fit = WindowBoundsPolicy.Fit(new Rect(900, 680, 880, 700),
            new System.Windows.Size(1100, 900), new Rect(0, 0, 1024, 720));
        Check(fit == new Rect(0, 0, 1024, 720), "Large window should fit in a smaller work area.");
        Rect leftMonitor = WindowBoundsPolicy.Fit(new Rect(-1500, 50, 900, 700),
            new System.Windows.Size(900, 700), new Rect(-1920, 0, 1280, 760));
        Check(leftMonitor.Left >= -1920 && leftMonitor.Right <= -640, "Negative-origin monitor should be respected.");
    }

    private static void TestNumericGlyphBounds()
    {
        foreach (int size in new[] { 16, 20, 24, 32, 40, 48 })
        foreach (int value in new[] { -1, 0, 9, 10, 99, 100 })
        {
            using Bitmap bitmap = BatteryIconDrawing.CreateNumericBitmap(new LogiDevice { BatteryPercentage = value }, size);
            bool hasInk = false;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;
                hasInk = true;
                Check(x > 0 && y > 0 && x < size - 1 && y < size - 1,
                    $"Numeric glyph {value} touches the {size}px icon edge.");
            }
            Check(hasInk, $"Numeric glyph {value} should render at {size}px.");
        }
    }

    private static void TestDeviceSettingsScopes()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "PowerTray-152-tests", Guid.NewGuid().ToString("N"));
        PowerTrayConstants.UserDataDirectoryOverrideForTests = tempDirectory;
        try
        {
            UserSettingsWrapper settings = new();
            settings.NumericDisplay = false;
            NotifyIconViewModel.ToggleNumericDisplaySetting(settings, "first");
            Check(!settings.NumericDisplay, "Device tray action must not change the global icon preference.");
            Check(settings.GetDeviceNumericDisplay("first") && !settings.GetDeviceNumericDisplay("second"),
                "Device tray action should affect only the selected device.");
            NotifyIconViewModel.ToggleNumericDisplaySetting(settings, null);
            Check(settings.NumericDisplay, "Global tray action should change the global icon preference.");

            settings.DefaultWindowsNotification = true;
            settings.DefaultTrayBlink = false;
            LocalizationService loc = new(settings);
            using LogiDeviceViewModel device = new(null!, settings, loc) { DeviceId = "first", DeviceName = "Fixture" };
            using DeviceSettingsItemViewModel item = new(device, settings, loc, null!, null!, _ => { });
            Check(item.FollowGlobalNotifications, "New device should inherit global notifications.");
            item.FollowGlobalNotifications = false;
            item.WindowsNotification = false;
            Check(!item.FollowGlobalNotifications, "Changing one notification should create a device override.");
            item.FollowGlobalNotifications = true;
            DeviceAlertSettings stored = settings.GetDeviceSettings("first");
            Check(!stored.WindowsNotification.HasValue && !stored.TrayBlink.HasValue,
                "Following global should clear both device notification overrides.");
            settings.DefaultWindowsNotification = false;
            Check(!item.WindowsNotification, "Device should follow later global changes.");
        }
        finally
        {
            PowerTrayConstants.UserDataDirectoryOverrideForTests = null;
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static void TestLocalizedLabels()
    {
        foreach (string language in new[] { "en-US", "zh-CN", "ja-JP" })
        {
            var catalog = LocalizationService.GetCatalogForTesting(language);
            foreach (string key in new[] { "MenuNumericIconGlobal", "MenuNumericIconDevice", "FollowGlobalNotifications", "TimeValidationError", "QuietHoursHint" })
                Check(catalog.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{language} is missing {key}.");
        }
    }
}
