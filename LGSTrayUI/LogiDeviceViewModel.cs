using CommunityToolkit.Mvvm.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayPrimitives.MessageStructs;
using System;
using System.ComponentModel;
using System.Globalization;

namespace LGSTrayUI
{
    public class LogiDeviceViewModelFactory
    {
        private readonly LogiDeviceIconFactory _logiDeviceIconFactory;
        private readonly UserSettingsWrapper _userSettings;
        private readonly LocalizationService _loc;

        public LogiDeviceViewModelFactory(LogiDeviceIconFactory logiDeviceIconFactory, UserSettingsWrapper userSettings, LocalizationService loc)
        {
            _logiDeviceIconFactory = logiDeviceIconFactory;
            _userSettings = userSettings;
            _loc = loc;
        }

        public LogiDeviceViewModel CreateViewModel(Action<LogiDeviceViewModel>? config = null)
        {
            LogiDeviceViewModel output = new(_logiDeviceIconFactory, _userSettings, _loc);
            config?.Invoke(output);

            return output;
        }
    }

    public partial class LogiDeviceViewModel : LogiDevice, IDisposable
    {
        private readonly LogiDeviceIconFactory _logiDeviceIconFactory;
        private readonly UserSettingsWrapper _userSettings;
        private readonly LocalizationService _loc;
        private readonly PropertyChangedEventHandler _localizationChangedHandler;
        private bool _disposed;

        [ObservableProperty]
        private bool _isChecked = false;

        [ObservableProperty]
        private bool _isOnline = true;

        public DateTimeOffset LastSeenUtc { get; private set; } = DateTimeOffset.MinValue;

        private LogiDeviceIcon? taskbarIcon;

        public string BaseDisplayName => _userSettings.GetDisplayName(DeviceId, DeviceName);
        public string DisplayName => IsOnline ? BaseDisplayName : $"{BaseDisplayName} ({_loc["Offline"]})";
        public string OriginalNameDisplay => _userSettings.GetOriginalName(DeviceId, DeviceName);
        public bool HasAlias => !string.IsNullOrWhiteSpace(_userSettings.GetAlias(DeviceId, DeviceName));
        public bool ShowOriginalName => HasAlias && !string.Equals(BaseDisplayName, OriginalNameDisplay, StringComparison.Ordinal);

        public string DisplayToolTipString => BatteryPercentage >= 0
            ?
#if DEBUG
            FormatToolTipDetail(DisplayName, $"{BatteryPercentage:f2}%{BatteryVoltageText()} - {LastUpdate}")
#else
            FormatToolTipDetail(DisplayName, $"{BatteryPercentage:f2}%{BatteryVoltageText()}")
#endif
            : FormatToolTipDetail(DisplayName, _loc["BatteryUnknown"]);

        internal static string FormatToolTipDetail(string displayName, string detail) =>
            $"{displayName}{GetToolTipSeparator(displayName)}{detail}";

        public LogiDeviceViewModel(LogiDeviceIconFactory logiDeviceIconFactory, UserSettingsWrapper userSettings, LocalizationService loc)
        {
            _logiDeviceIconFactory = logiDeviceIconFactory;
            _userSettings = userSettings;
            _loc = loc;
            _localizationChangedHandler = (_, _) => RefreshDisplayProperties();
            _userSettings.DeviceSettingsChanged += OnDeviceSettingsChanged;
            _loc.PropertyChanged += _localizationChangedHandler;
        }

        private string BatteryVoltageText() => BatteryVoltage > 0 ? $", {BatteryVoltage:0.00} V" : string.Empty;

        private static string GetToolTipSeparator(string displayName)
        {
            string trimmed = displayName.TrimEnd();
            char last = trimmed.Length > 0 ? trimmed[^1] : '\0';

            return IsFullWidthOrCjkEnding(last) ? "，" : ", ";
        }

        private static bool IsFullWidthOrCjkEnding(char value)
        {
            if (value == '\0')
            {
                return false;
            }

            int code = value;
            return code is >= 0x3000 and <= 0x303F // CJK symbols and punctuation
                or >= 0x3040 and <= 0x30FF // Hiragana and Katakana
                or >= 0x3100 and <= 0x312F // Bopomofo
                or >= 0x31F0 and <= 0x31FF // Katakana phonetic extensions
                or >= 0x3400 and <= 0x4DBF // CJK Unified Ideographs Extension A
                or >= 0x4E00 and <= 0x9FFF // CJK Unified Ideographs
                or >= 0xAC00 and <= 0xD7AF // Hangul syllables
                or >= 0xF900 and <= 0xFAFF // CJK compatibility ideographs
                or >= 0xFF00 and <= 0xFFEF // Halfwidth and fullwidth forms
                || (CharUnicodeInfo.GetUnicodeCategory(value) is UnicodeCategory.OtherLetter && code >= 0x2E80);
        }

        private void OnDeviceSettingsChanged(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId == DeviceId)
            {
                RefreshDisplayProperties();
            }
        }

        partial void OnIsCheckedChanged(bool oldValue, bool newValue)
        {
            UpdateTaskbarIconState();
        }

        partial void OnIsOnlineChanged(bool oldValue, bool newValue)
        {
            UpdateTaskbarIconState();
            RefreshDisplayProperties();
        }

        private void UpdateTaskbarIconState()
        {
            if (IsChecked && IsOnline)
            {
                taskbarIcon ??= _logiDeviceIconFactory.CreateDeviceIcon(this);
            }
            else
            {
                taskbarIcon?.Dispose();
                taskbarIcon = null;
            }
        }

        private void RefreshDisplayProperties()
        {
            OnPropertyChanged(nameof(BaseDisplayName));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(OriginalNameDisplay));
            OnPropertyChanged(nameof(HasAlias));
            OnPropertyChanged(nameof(ShowOriginalName));
            OnPropertyChanged(nameof(DisplayToolTipString));
        }

        public void MarkPresence()
        {
            LastSeenUtc = DateTimeOffset.UtcNow;
            IsOnline = true;
            OnPropertyChanged(nameof(LastSeenUtc));
            OnPropertyChanged(nameof(DisplayToolTipString));
        }

        public void MarkOffline()
        {
            IsOnline = false;
            OnPropertyChanged(nameof(DisplayToolTipString));
        }

        public void UpdateState(InitMessage initMessage)
        {
            if (string.IsNullOrEmpty(DeviceId) || DeviceId == NOT_FOUND)
            {
                DeviceId = initMessage.deviceId;
            }

            DeviceName = initMessage.deviceName;
            HasBattery = initMessage.hasBattery;
            DeviceType = initMessage.deviceType;
            _userSettings.GetDeviceSettings(DeviceId, DeviceName, DeviceType);
            RefreshDisplayProperties();
        }

        public void UpdateState(UpdateMessage updateMessage)
        {
            BatteryPercentage = double.IsFinite(updateMessage.batteryPercentage)
                ? Math.Clamp(updateMessage.batteryPercentage, 0, 100)
                : -1;
            PowerSupplyStatus = updateMessage.powerSupplyStatus;
            BatteryVoltage = updateMessage.batteryMVolt / 1000.0;
            BatteryMileage = updateMessage.Mileage;
            LastUpdate = updateMessage.updateTime;
            OnPropertyChanged(nameof(DisplayToolTipString));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _userSettings.DeviceSettingsChanged -= OnDeviceSettingsChanged;
            _loc.PropertyChanged -= _localizationChangedHandler;
            taskbarIcon?.Dispose();
            taskbarIcon = null;
        }
    }
}
