using System;
using System.IO;

namespace LGSTrayUI;

public static class PowerTrayConstants
{
    public const string ProductName = "PowerTray";
    public const string AppUserModelId = "PowerTray.NativeBattery";
    public const string MainExecutable = "PowerTray.exe";
    public const string HidExecutable = "PowerTrayHID.exe";
    public const string AutoStartRegValue = "PowerTray";
    public const string LegacyAutoStartRegValue = "LGSTrayGUI";

    public static string UserDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName);

    public static string SettingsPath => Path.Combine(UserDataDirectory, "settings.json");
}
