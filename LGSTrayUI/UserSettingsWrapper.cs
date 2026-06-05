using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;

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

        public DeviceAlertSettings GetDeviceSettings(string deviceId, string deviceName = "")
        {
            if (!_settings.Devices.TryGetValue(deviceId, out DeviceAlertSettings? deviceSettings))
            {
                deviceSettings = new();
                _settings.Devices[deviceId] = deviceSettings;
            }

            if (!string.IsNullOrWhiteSpace(deviceName) && deviceSettings.LastDeviceName != deviceName)
            {
                deviceSettings.LastDeviceName = deviceName;
                Save();
            }

            return deviceSettings;
        }

        public string GetDisplayName(string deviceId, string deviceName)
        {
            string alias = GetDeviceSettings(deviceId, deviceName).Alias;
            return string.IsNullOrWhiteSpace(alias) ? deviceName : alias.Trim();
        }

        public int GetThreshold(string deviceId) =>
            GetDeviceSettings(deviceId).ThresholdPercent ?? _settings.GlobalAlerts.ThresholdPercent;

        public bool GetWindowsNotificationEnabled(string deviceId) =>
            GetDeviceSettings(deviceId).WindowsNotification ?? _settings.GlobalAlerts.WindowsNotification;

        public bool GetTrayBlinkEnabled(string deviceId) =>
            GetDeviceSettings(deviceId).TrayBlink ?? _settings.GlobalAlerts.TrayBlink;

        public DateTimeOffset? GetPauseUntil(string deviceId) =>
            GetDeviceSettings(deviceId).PauseUntil;

        public void SetDeviceAlias(string deviceId, string alias)
        {
            GetDeviceSettings(deviceId).Alias = alias.Trim();
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

        public void SetDevicePauseUntil(string deviceId, DateTimeOffset? pauseUntil)
        {
            GetDeviceSettings(deviceId).PauseUntil = pauseUntil;
            SaveDevice(deviceId);
        }

        public void RestoreDeviceDefaults(string deviceId)
        {
            if (_settings.Devices.TryGetValue(deviceId, out DeviceAlertSettings? deviceSettings))
            {
                string alias = deviceSettings.Alias;
                string lastName = deviceSettings.LastDeviceName;
                _settings.Devices[deviceId] = new DeviceAlertSettings
                {
                    Alias = alias,
                    LastDeviceName = lastName,
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
                        settings.Language = NormalizeLanguage(settings.Language);
                        settings.GlobalAlerts.ThresholdPercent = ClampPercent(settings.GlobalAlerts.ThresholdPercent);
                        return settings;
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
            File.WriteAllText(PowerTrayConstants.SettingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
        }

        private static string NormalizeLanguage(string? language)
        {
            return language?.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) == true ? "zh-CN" : "en-US";
        }

        private static int ClampPercent(int value) => Math.Clamp(value, 1, 100);

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
