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

        public LogiDeviceViewModelFactory(LogiDeviceIconFactory logiDeviceIconFactory, UserSettingsWrapper userSettings)
        {
            _logiDeviceIconFactory = logiDeviceIconFactory;
            _userSettings = userSettings;
        }

        public LogiDeviceViewModel CreateViewModel(Action<LogiDeviceViewModel>? config = null)
        {
            LogiDeviceViewModel output = new(_logiDeviceIconFactory, _userSettings);
            config?.Invoke(output);

            return output;
        }
    }

    public partial class LogiDeviceViewModel : LogiDevice
    {
        private readonly LogiDeviceIconFactory _logiDeviceIconFactory;
        private readonly UserSettingsWrapper _userSettings;

        [ObservableProperty]
        private bool _isChecked = false;

        private LogiDeviceIcon? taskbarIcon;

        public string DisplayName => _userSettings.GetDisplayName(DeviceId, DeviceName);

        public string DisplayToolTipString =>
#if DEBUG
            $"{DisplayName}, {BatteryPercentage:f2}% - {LastUpdate}";
#else
            $"{DisplayName}, {BatteryPercentage:f2}%";
#endif

        public LogiDeviceViewModel(LogiDeviceIconFactory logiDeviceIconFactory, UserSettingsWrapper userSettings)
        {
            _logiDeviceIconFactory = logiDeviceIconFactory;
            _userSettings = userSettings;
            _userSettings.DeviceSettingsChanged += OnDeviceSettingsChanged;
        }

        private void OnDeviceSettingsChanged(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId == DeviceId)
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplayToolTipString));
            }
        }

        partial void OnIsCheckedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                taskbarIcon ??= _logiDeviceIconFactory.CreateDeviceIcon(this);
            }
            else
            {
                taskbarIcon?.Dispose();
                taskbarIcon = null;
            }
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
            _userSettings.GetDeviceSettings(DeviceId, DeviceName);
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DisplayToolTipString));
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
