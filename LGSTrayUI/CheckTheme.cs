using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Security.Principal;

namespace LGSTrayUI
{
    public static class CheckTheme
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppThemeRegistryValueName = "AppsUseLightTheme";
        private const string WindowsThemeRegistryValueName = "SystemUsesLightTheme";

        private static bool _appLightTheme = true;
        private static bool _windowsLightTheme = true;
        private static string _themeMode = "system";
        public static bool AppLightTheme => _appLightTheme;
        public static bool WindowsLightTheme => _windowsLightTheme;
        public static bool LightTheme => _themeMode switch
        {
            "light" => true,
            "dark" => false,
            _ => _appLightTheme,
        };
        public static bool TaskbarLightTheme => _windowsLightTheme;
        public static string ThemeMode => _themeMode;

        public static string ThemeSuffix
        {
            get
            {
                return LightTheme ? "" : "_dark";
            }
        }

        public static string TaskbarThemeSuffix => TaskbarLightTheme ? "" : "_dark";

        public static event PropertyChangedEventHandler? StaticPropertyChanged;

        static CheckTheme()
        {
            StartWatcher(AppThemeRegistryValueName, (_, _) => UpdateAppThemeStatus());
            StartWatcher(WindowsThemeRegistryValueName, (_, _) => UpdateWindowsThemeStatus());
            UpdateAppThemeStatus();
            UpdateWindowsThemeStatus();
        }

        public static void SetThemeMode(string? themeMode)
        {
            string normalized = themeMode?.ToLowerInvariant() switch
            {
                "light" => "light",
                "dark" => "dark",
                _ => "system",
            };

            if (_themeMode == normalized)
            {
                return;
            }

            bool previous = LightTheme;
            _themeMode = normalized;
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(ThemeMode)));
            if (previous != LightTheme)
            {
                StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(LightTheme)));
                StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(ThemeSuffix)));
            }
        }

        private static void StartWatcher(string valueName, EventArrivedEventHandler handler)
        {
            try
            {
                var currentUser = WindowsIdentity.GetCurrent();
                string query = string.Format(
                    CultureInfo.InvariantCulture,
                    @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
                    currentUser.User!.Value,
                    RegistryKeyPath.Replace(@"\", @"\\"),
                    valueName);

                var watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += handler;
                watcher.Start();
            }
            catch
            {
                // Theme watching is best-effort; startup reads below still provide defaults.
            }
        }

        private static void UpdateAppThemeStatus()
        {
            bool previous = LightTheme;
            _appLightTheme = ReadThemeFlag(AppThemeRegistryValueName);
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(AppLightTheme)));
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(LightTheme)));
            if (previous != LightTheme)
            {
                StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(ThemeSuffix)));
            }
        }

        private static void UpdateWindowsThemeStatus()
        {
            bool previous = TaskbarLightTheme;
            _windowsLightTheme = ReadThemeFlag(WindowsThemeRegistryValueName);
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(WindowsLightTheme)));
            StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(TaskbarLightTheme)));
            if (previous != TaskbarLightTheme)
            {
                StaticPropertyChanged?.Invoke(typeof(CheckTheme), new(nameof(TaskbarThemeSuffix)));
            }
        }

        private static bool ReadThemeFlag(string valueName)
        {
            using var regPath = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            object? rawValue = regPath?.GetValue(valueName, 1);
            return rawValue is int intValue ? intValue != 0 : true;
        }
    }
}
