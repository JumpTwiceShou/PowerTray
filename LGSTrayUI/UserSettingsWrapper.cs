using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using LGSTrayPrimitives;

namespace LGSTrayUI
{
    public partial class UserSettingsWrapper : ObservableObject
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly PowerTrayUserSettings _settings;
        private readonly HashSet<string> _pausedUntilNextLaunch = [];

        public UserSettingsWrapper()
        {
            _settings = Load();
            _settings.AutoStart = ReadAutoStart();
            Save();
        }

        public IEnumerable<string> SelectedDevices => _settings.SelectedDevices;
        public PowerTrayUserSettings Snapshot => _settings;

        public string Language
        {
            get => NormalizeLanguage(_settings.Language);
            set
            {
                string normalized = NormalizeLanguage(value);
                if (_settings.Language == normalized)
                {
                    return;
                }

                _settings.Language = normalized;
                Save();
                OnPropertyChanged();
            }
        }

        public string ThemeMode
        {
            get => NormalizeThemeMode(_settings.ThemeMode);
            set
            {
                string normalized = NormalizeThemeMode(value);
                if (_settings.ThemeMode == normalized)
                {
                    return;
                }

                _settings.ThemeMode = normalized;
                Save();
                OnPropertyChanged();
            }
        }

        public string UiScaleMode
        {
            get => NormalizeUiScaleMode(_settings.UiScaleMode);
            set
            {
                string normalized = NormalizeUiScaleMode(value);
                if (_settings.UiScaleMode == normalized)
                {
                    return;
                }

                _settings.UiScaleMode = normalized;
                Save();
                OnPropertyChanged();
            }
        }

        public bool NumericDisplay
        {
            get => _settings.NumericDisplay;
            set
            {
                if (_settings.NumericDisplay == value)
                {
                    return;
                }

                _settings.NumericDisplay = value;
                Save();
                OnPropertyChanged();
                OnDeviceSettingsChanged();
            }
        }

        public bool AutoStart
        {
            get => ReadAutoStart();
            set
            {
                WriteAutoStart(value);
                _settings.AutoStart = value;
                Save();
                OnPropertyChanged();
            }
        }

        public bool AutoCheckUpdates
        {
            get => _settings.AutoCheckUpdates;
            set
            {
                if (_settings.AutoCheckUpdates == value)
                {
                    return;
                }

                _settings.AutoCheckUpdates = value;
                Save();
                OnPropertyChanged();
            }
        }

        public int DefaultThresholdPercent
        {
            get => _settings.GlobalAlerts.ThresholdPercent;
            set
            {
                _settings.GlobalAlerts.ThresholdPercent = ClampPercent(value);
                Save();
                OnPropertyChanged();
                OnDeviceSettingsChanged();
            }
        }

        public bool DefaultWindowsNotification
        {
            get => _settings.GlobalAlerts.WindowsNotification;
            set
            {
                _settings.GlobalAlerts.WindowsNotification = value;
                Save();
                OnPropertyChanged();
                OnDeviceSettingsChanged();
            }
        }

        public bool DefaultTrayBlink
        {
            get => _settings.GlobalAlerts.TrayBlink;
            set
            {
                _settings.GlobalAlerts.TrayBlink = value;
                Save();
                OnPropertyChanged();
                OnDeviceSettingsChanged();
            }
        }

        public bool QuietHoursEnabled
        {
            get => _settings.GlobalAlerts.QuietHoursEnabled;
            set
            {
                _settings.GlobalAlerts.QuietHoursEnabled = value;
                Save();
                OnPropertyChanged();
            }
        }

        public string QuietHoursStart
        {
            get => _settings.GlobalAlerts.QuietHoursStart;
            set
            {
                _settings.GlobalAlerts.QuietHoursStart = NormalizeTime(value, "23:00");
                Save();
                OnPropertyChanged();
            }
        }

        public string QuietHoursEnd
        {
            get => _settings.GlobalAlerts.QuietHoursEnd;
            set
            {
                _settings.GlobalAlerts.QuietHoursEnd = NormalizeTime(value, "08:00");
                Save();
                OnPropertyChanged();
            }
        }

        public bool SuppressNotificationsWhenFullscreen
        {
            get => _settings.GlobalAlerts.SuppressNotificationsWhenFullscreen;
            set
            {
                _settings.GlobalAlerts.SuppressNotificationsWhenFullscreen = value;
                Save();
                OnPropertyChanged();
            }
        }

        public event Action<string>? DeviceSettingsChanged;

        public void AddDevice(string deviceId)
        {
            if (_settings.SelectedDevices.Contains(deviceId))
            {
                return;
            }

            _settings.SelectedDevices.Add(deviceId);
            Save();
            OnPropertyChanged(nameof(SelectedDevices));
        }

        public void RemoveDevice(string deviceId)
        {
            _settings.SelectedDevices.Remove(deviceId);
            Save();
            OnPropertyChanged(nameof(SelectedDevices));
        }

        public void RemoveDeviceHistory(string deviceId)
        {
            bool changed = _settings.SelectedDevices.Remove(deviceId);
            changed |= _settings.Devices.Remove(deviceId);
            if (!changed)
            {
                return;
            }

            Save();
            OnPropertyChanged(nameof(SelectedDevices));
            OnDeviceSettingsChanged(deviceId);
        }

        public DeviceAlertSettings GetDeviceSettings(string deviceId, string deviceName = "", DeviceType? deviceType = null)
        {
            if (!_settings.Devices.TryGetValue(deviceId, out DeviceAlertSettings? deviceSettings))
            {
                deviceSettings = new();
                _settings.Devices[deviceId] = deviceSettings;
            }

            bool changed = false;
            if (IsPersistableDeviceName(deviceName) && deviceSettings.LastDeviceName != deviceName)
            {
                deviceSettings.LastDeviceName = deviceName;
                changed = true;
            }

            if (deviceType.HasValue && deviceSettings.LastDeviceType != deviceType.Value)
            {
                deviceSettings.LastDeviceType = deviceType.Value;
                changed = true;
            }

            if (changed)
            {
                Save();
            }

            return deviceSettings;
        }

        public DeviceType GetDeviceType(string deviceId, string deviceName = "")
        {
            DeviceAlertSettings deviceSettings = GetDeviceSettings(deviceId, deviceName);
            if (deviceSettings.LastDeviceType.HasValue)
            {
                return deviceSettings.LastDeviceType.Value;
            }

            DeviceType? inferredType = InferDeviceType(deviceSettings.LastDeviceName) ?? InferDeviceType(deviceName);
            if (inferredType.HasValue)
            {
                deviceSettings.LastDeviceType = inferredType.Value;
                SaveDevice(deviceId);
                return inferredType.Value;
            }

            return default;
        }

        public string GetDisplayName(string deviceId, string deviceName)
        {
            string alias = GetDeviceSettings(deviceId, deviceName).Alias;
            return string.IsNullOrWhiteSpace(alias) ? GetOriginalName(deviceId, deviceName) : alias.Trim();
        }

        public string GetOriginalName(string deviceId, string deviceName)
        {
            DeviceAlertSettings deviceSettings = GetDeviceSettings(deviceId, deviceName);
            if (IsPersistableDeviceName(deviceName))
            {
                return deviceName;
            }

            if (IsPersistableDeviceName(deviceSettings.LastDeviceName))
            {
                return deviceSettings.LastDeviceName;
            }

            return string.IsNullOrWhiteSpace(deviceId) ? "Unknown Logitech device" : deviceId;
        }

        public string GetAlias(string deviceId, string deviceName = "") =>
            GetDeviceSettings(deviceId, deviceName).Alias;

        public int GetThreshold(string deviceId) =>
            GetDeviceSettings(deviceId).ThresholdPercent ?? _settings.GlobalAlerts.ThresholdPercent;

        public bool HasDeviceThreshold(string deviceId) =>
            GetDeviceSettings(deviceId).ThresholdPercent.HasValue;

        public bool GetWindowsNotificationEnabled(string deviceId) =>
            GetDeviceSettings(deviceId).WindowsNotification ?? _settings.GlobalAlerts.WindowsNotification;

        public bool GetTrayBlinkEnabled(string deviceId) =>
            GetDeviceSettings(deviceId).TrayBlink ?? _settings.GlobalAlerts.TrayBlink;

        public bool GetDeviceNumericDisplay(string deviceId) =>
            IsPersistableDeviceId(deviceId)
                ? GetDeviceSettings(deviceId).NumericDisplay ?? _settings.NumericDisplay
                : _settings.NumericDisplay;

        public bool HasDeviceNumericDisplayOverride(string deviceId) =>
            IsPersistableDeviceId(deviceId) &&
            GetDeviceSettings(deviceId).NumericDisplay.HasValue;

        public DateTimeOffset? GetPauseUntil(string deviceId) =>
            GetDeviceSettings(deviceId).PauseUntil;

        public bool IsPausedUntilNextLaunch(string deviceId) =>
            _pausedUntilNextLaunch.Contains(deviceId);

        public bool IsDevicePaused(string deviceId, DateTimeOffset now) =>
            IsPausedUntilNextLaunch(deviceId) ||
            (GetPauseUntil(deviceId) is { } pauseUntil && pauseUntil > now);

        public void SetDeviceAlias(string deviceId, string alias)
        {
            GetDeviceSettings(deviceId).Alias = NormalizeAlias(alias);
            SaveDevice(deviceId);
        }

        public void SetDeviceThreshold(string deviceId, int? threshold)
        {
            GetDeviceSettings(deviceId).ThresholdPercent = threshold.HasValue ? ClampPercent(threshold.Value) : null;
            SaveDevice(deviceId);
        }

        public void SetDeviceWindowsNotification(string deviceId, bool? enabled)
        {
            GetDeviceSettings(deviceId).WindowsNotification = enabled;
            SaveDevice(deviceId);
        }

        public void SetDeviceTrayBlink(string deviceId, bool? enabled)
        {
            GetDeviceSettings(deviceId).TrayBlink = enabled;
            SaveDevice(deviceId);
        }

        public void SetDeviceNumericDisplayOverride(string deviceId, bool? enabled)
        {
            if (!IsPersistableDeviceId(deviceId))
            {
                return;
            }

            GetDeviceSettings(deviceId).NumericDisplay = enabled;
            SaveDevice(deviceId);
        }

        public void SetDevicePauseUntil(string deviceId, DateTimeOffset? pauseUntil)
        {
            _pausedUntilNextLaunch.Remove(deviceId);
            GetDeviceSettings(deviceId).PauseUntil = pauseUntil;
            SaveDevice(deviceId);
        }

        public void SetDevicePauseUntilNextLaunch(string deviceId)
        {
            GetDeviceSettings(deviceId).PauseUntil = null;
            _pausedUntilNextLaunch.Add(deviceId);
            SaveDevice(deviceId);
        }

        public bool MigrateDeviceId(string oldDeviceId, string newDeviceId, string deviceName, DeviceType? deviceType = null)
        {
            if (string.IsNullOrWhiteSpace(oldDeviceId) ||
                string.IsNullOrWhiteSpace(newDeviceId) ||
                oldDeviceId == newDeviceId)
            {
                return false;
            }

            bool changed = false;
            bool wasSelected = _settings.SelectedDevices.Remove(oldDeviceId);
            if (wasSelected && !_settings.SelectedDevices.Contains(newDeviceId))
            {
                _settings.SelectedDevices.Add(newDeviceId);
                changed = true;
            }
            else
            {
                changed |= wasSelected;
            }

            if (_settings.Devices.TryGetValue(oldDeviceId, out DeviceAlertSettings? oldSettings))
            {
                if (_settings.Devices.TryGetValue(newDeviceId, out DeviceAlertSettings? newSettings))
                {
                    MergeDeviceSettings(newSettings, oldSettings);
                }
                else
                {
                    _settings.Devices[newDeviceId] = oldSettings;
                }

                _settings.Devices.Remove(oldDeviceId);
                changed = true;
            }

            if (IsPersistableDeviceName(deviceName))
            {
                DeviceAlertSettings newDeviceSettings = GetDeviceSettings(newDeviceId);
                if (newDeviceSettings.LastDeviceName != deviceName)
                {
                    newDeviceSettings.LastDeviceName = deviceName;
                    changed = true;
                }
            }

            if (deviceType.HasValue)
            {
                DeviceAlertSettings newDeviceSettings = GetDeviceSettings(newDeviceId);
                if (newDeviceSettings.LastDeviceType != deviceType.Value)
                {
                    newDeviceSettings.LastDeviceType = deviceType.Value;
                    changed = true;
                }
            }

            if (_pausedUntilNextLaunch.Remove(oldDeviceId))
            {
                _pausedUntilNextLaunch.Add(newDeviceId);
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            Save();
            OnPropertyChanged(nameof(SelectedDevices));
            OnDeviceSettingsChanged(oldDeviceId);
            OnDeviceSettingsChanged(newDeviceId);
            return true;
        }

        public void RestoreDeviceDefaults(string deviceId)
        {
            if (_settings.Devices.TryGetValue(deviceId, out DeviceAlertSettings? deviceSettings))
            {
                string alias = deviceSettings.Alias;
                string lastName = deviceSettings.LastDeviceName;
                DeviceType? lastType = deviceSettings.LastDeviceType;
                _settings.Devices[deviceId] = new DeviceAlertSettings
                {
                    Alias = alias,
                    LastDeviceName = lastName,
                    LastDeviceType = lastType,
                };
                SaveDevice(deviceId);
            }
        }

        public string ExportSettingsSummary()
        {
            return JsonSerializer.Serialize(_settings, JsonOptions);
        }

        private void SaveDevice(string deviceId)
        {
            Save();
            OnDeviceSettingsChanged(deviceId);
        }

        private static void MergeDeviceSettings(DeviceAlertSettings target, DeviceAlertSettings source)
        {
            if (string.IsNullOrWhiteSpace(target.Alias) && !string.IsNullOrWhiteSpace(source.Alias))
            {
                target.Alias = source.Alias;
            }

            if (string.IsNullOrWhiteSpace(target.LastDeviceName) && !string.IsNullOrWhiteSpace(source.LastDeviceName))
            {
                target.LastDeviceName = source.LastDeviceName;
            }

            target.LastDeviceType ??= source.LastDeviceType;
            target.ThresholdPercent ??= source.ThresholdPercent;
            target.WindowsNotification ??= source.WindowsNotification;
            target.TrayBlink ??= source.TrayBlink;
            target.NumericDisplay ??= source.NumericDisplay;
            target.PauseUntil ??= source.PauseUntil;
        }

        private void OnDeviceSettingsChanged(string deviceId = "")
        {
            DeviceSettingsChanged?.Invoke(deviceId);
            OnPropertyChanged(nameof(Snapshot));
        }

        private static PowerTrayUserSettings Load()
        {
            try
            {
                if (File.Exists(PowerTrayConstants.SettingsPath))
                {
                    PowerTrayUserSettings? settings = JsonSerializer.Deserialize<PowerTrayUserSettings>(
                        File.ReadAllText(PowerTrayConstants.SettingsPath),
                        JsonOptions
                    );

                    if (settings != null)
                    {
                        return NormalizeSettings(settings);
                    }
                }
            }
            catch
            {
                string backupPath = PowerTrayConstants.SettingsPath + ".broken";
                try { File.Move(PowerTrayConstants.SettingsPath, backupPath, true); } catch { }
            }

            return MigrateLegacySettings();
        }

        private static PowerTrayUserSettings NormalizeSettings(PowerTrayUserSettings settings)
        {
            settings.Language = NormalizeLanguage(settings.Language);
            settings.ThemeMode = NormalizeThemeMode(settings.ThemeMode);
            settings.UiScaleMode = NormalizeUiScaleMode(settings.UiScaleMode);
            settings.SelectedDevices ??= [];
            settings.SelectedDevices = settings.SelectedDevices
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            settings.GlobalAlerts ??= new();
            settings.GlobalAlerts.ThresholdPercent = ClampPercent(settings.GlobalAlerts.ThresholdPercent);
            settings.GlobalAlerts.QuietHoursStart = NormalizeTime(settings.GlobalAlerts.QuietHoursStart, "23:00");
            settings.GlobalAlerts.QuietHoursEnd = NormalizeTime(settings.GlobalAlerts.QuietHoursEnd, "08:00");
            settings.Devices ??= [];

            foreach (string deviceId in settings.Devices
                .Where(x => x.Value == null)
                .Select(x => x.Key)
                .ToArray())
            {
                settings.Devices[deviceId] = new();
            }

            foreach (DeviceAlertSettings deviceSettings in settings.Devices.Values)
            {
                deviceSettings.Alias = NormalizeAlias(deviceSettings.Alias);
                deviceSettings.LastDeviceName ??= string.Empty;
                deviceSettings.LastDeviceType ??= InferDeviceType(deviceSettings.LastDeviceName);
                if (deviceSettings.ThresholdPercent.HasValue)
                {
                    deviceSettings.ThresholdPercent = ClampPercent(deviceSettings.ThresholdPercent.Value);
                }
            }

            return settings;
        }

        private static PowerTrayUserSettings MigrateLegacySettings()
        {
            PowerTrayUserSettings settings = new();

            try
            {
                settings.NumericDisplay = Properties.Settings.Default.NumericDisplay;
                StringCollection? selectedDevices = Properties.Settings.Default.SelectedDevices;
                if (selectedDevices != null)
                {
                    foreach (string? deviceId in selectedDevices)
                    {
                        if (!string.IsNullOrWhiteSpace(deviceId) && !settings.SelectedDevices.Contains(deviceId))
                        {
                            settings.SelectedDevices.Add(deviceId);
                        }
                    }
                }
            }
            catch
            {
                // Legacy settings are optional; ignore corrupted values.
            }

            return settings;
        }

        private void Save()
        {
            Directory.CreateDirectory(PowerTrayConstants.UserDataDirectory);
            string tempPath = PowerTrayConstants.SettingsPath + ".tmp";
            string backupPath = PowerTrayConstants.SettingsPath + ".bak";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_settings, JsonOptions));

            if (File.Exists(PowerTrayConstants.SettingsPath))
            {
                File.Replace(tempPath, PowerTrayConstants.SettingsPath, backupPath, true);
                return;
            }

            File.Move(tempPath, PowerTrayConstants.SettingsPath, true);
        }

        private static string NormalizeLanguage(string? language)
        {
            return language switch
            {
                string value when value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) => "zh-CN",
                string value when value.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) => "ja-JP",
                _ => "en-US",
            };
        }

        private static string NormalizeThemeMode(string? themeMode)
        {
            return themeMode?.ToLowerInvariant() switch
            {
                "light" => "light",
                "dark" => "dark",
                _ => "system",
            };
        }

        private static string NormalizeUiScaleMode(string? uiScaleMode)
        {
            return uiScaleMode?.ToLowerInvariant() switch
            {
                "small" => "small",
                "large" => "large",
                "maximum" => "maximum",
                _ => "standard",
            };
        }

        private static bool IsPersistableDeviceName(string? deviceName)
        {
            return !string.IsNullOrWhiteSpace(deviceName) &&
                   !deviceName.Equals("NOT FOUND", StringComparison.OrdinalIgnoreCase) &&
                   !deviceName.Equals("Not Initialised", StringComparison.OrdinalIgnoreCase) &&
                   !deviceName.Equals("Not Initialized", StringComparison.OrdinalIgnoreCase);
        }

        private static DeviceType? InferDeviceType(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return null;
            }

            string normalized = deviceName.ToUpperInvariant();
            if (normalized.Contains("HEADSET", StringComparison.Ordinal) ||
                normalized.Contains("LIGHTSPEED GAMING HEADSET", StringComparison.Ordinal))
            {
                return DeviceType.Headset;
            }

            if (normalized.Contains("MOUSE", StringComparison.Ordinal) ||
                normalized.Contains("SUPERSTRIKE", StringComparison.Ordinal))
            {
                return DeviceType.Mouse;
            }

            if (normalized.Contains("KEYBOARD", StringComparison.Ordinal))
            {
                return DeviceType.Keyboard;
            }

            return null;
        }

        private static bool IsPersistableDeviceId(string? deviceId)
        {
            return !string.IsNullOrWhiteSpace(deviceId) &&
                   !deviceId.Equals("NOT FOUND", StringComparison.OrdinalIgnoreCase);
        }

        private static int ClampPercent(int value) => Math.Clamp(value, 1, 100);

        private static string NormalizeAlias(string? alias) =>
            alias?.TrimStart() ?? string.Empty;

        private static string NormalizeTime(string value, string fallback)
        {
            return TimeSpan.TryParse(value, out TimeSpan parsed)
                ? parsed.ToString(@"hh\:mm")
                : fallback;
        }

        private static bool ReadAutoStart()
        {
            using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);
            return registryKey?.GetValue(PowerTrayConstants.AutoStartRegValue) != null ||
                   registryKey?.GetValue(PowerTrayConstants.LegacyAutoStartRegValue) != null;
        }

        private static void WriteAutoStart(bool enabled)
        {
            using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);
            if (registryKey == null)
            {
                return;
            }

            registryKey.DeleteValue(PowerTrayConstants.LegacyAutoStartRegValue, false);
            if (enabled)
            {
                string exePath = Path.Combine(AppContext.BaseDirectory, PowerTrayConstants.MainExecutable);
                if (!File.Exists(exePath))
                {
                    exePath = Environment.ProcessPath ?? exePath;
                }

                registryKey.SetValue(PowerTrayConstants.AutoStartRegValue, $"\"{exePath}\"");
            }
            else
            {
                registryKey.DeleteValue(PowerTrayConstants.AutoStartRegValue, false);
            }
        }

        private const string AutoStartRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    }
}
