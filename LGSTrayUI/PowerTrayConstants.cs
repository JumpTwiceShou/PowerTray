using System;
using System.IO;

namespace LGSTrayUI;

public static class PowerTrayConstants
{
    internal static string? UserDataDirectoryOverrideForTests { get; set; }

    public const string ProductName = "PowerTray";
    public const string AppUserModelId = "PowerTray.NativeBattery";
    public const string MainExecutable = "PowerTray.exe";
    public const string HidExecutable = "PowerTrayHID.exe";
    public const string AutoStartRegValue = "PowerTray";
    public const string LegacyAutoStartRegValue = "LGSTrayGUI";

    public static string UserDataDirectory =>
        !string.IsNullOrWhiteSpace(UserDataDirectoryOverrideForTests)
            ? UserDataDirectoryOverrideForTests
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName);

    public static string SettingsPath => Path.Combine(UserDataDirectory, "settings.json");
}
