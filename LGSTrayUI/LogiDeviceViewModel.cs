using CommunityToolkit.Mvvm.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayPrimitives.MessageStructs;
using System;

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

    public partial class LogiDeviceViewModel : LogiDevice
    {
        private readonly LogiDeviceIconFactory _logiDeviceIconFactory;
        private readonly UserSettingsWrapper _userSettings;
        private readonly LocalizationService _loc;

        [ObservableProperty]
        private bool _isChecked = false;

        [ObservableProperty]
        private bool _isOnline = true;

        public long LastPresenceEpoch { get; private set; } = -1;
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
            $"{DisplayName}, {BatteryPercentage:f2}% - {LastUpdate}"
#else
            $"{DisplayName}, {BatteryPercentage:f2}%"
#endif
            : $"{DisplayName}, {_loc["BatteryUnknown"]}";

        public LogiDeviceViewModel(LogiDeviceIconFactory logiDeviceIconFactory, UserSettingsWrapper userSettings, LocalizationService loc)
        {
            _logiDeviceIconFactory = logiDeviceIconFactory;
            _userSettings = userSettings;
            _loc = loc;
            _userSettings.DeviceSettingsChanged += OnDeviceSettingsChanged;
            _loc.PropertyChanged += (_, _) => RefreshDisplayProperties();
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

        public void MarkPresence(long epoch)
        {
            LastPresenceEpoch = epoch;
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
            BatteryPercentage = updateMessage.batteryPercentage;
            PowerSupplyStatus = updateMessage.powerSupplyStatus;
            BatteryVoltage = updateMessage.batteryMVolt / 1000.0;
            BatteryMileage = updateMessage.Mileage;
            LastUpdate = updateMessage.updateTime;
            OnPropertyChanged(nameof(DisplayToolTipString));
        }
    }
}
