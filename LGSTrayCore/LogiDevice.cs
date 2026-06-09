using CommunityToolkit.Mvvm.ComponentModel;
using LGSTrayPrimitives;
using System.Globalization;
using System.Security;

namespace LGSTrayCore
{
    public partial class LogiDevice : ObservableObject
    {
        public const string NOT_FOUND = "NOT FOUND";

        [ObservableProperty]
        private DeviceType _deviceType;

        [ObservableProperty]
        private string _deviceId = NOT_FOUND;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private string _deviceName = NOT_FOUND;

        [ObservableProperty]
        private bool _hasBattery = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryPercentage = -1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryVoltage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private double _batteryMileage;


        [ObservableProperty]
        private PowerSupplyStatus _powerSupplyStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolTipString))]
        private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;

        public string ToolTipString
        {
            get
            {
#if DEBUG
                return $"{DeviceName}, {BatteryPercentage:f2}% - {LastUpdate}";
#else
                return $"{DeviceName}, {BatteryPercentage:f2}%";
#endif
            }
        }

        public Func<Task>? UpdateBatteryFunc;
        public async Task UpdateBatteryAsync()
        {
            if (UpdateBatteryFunc != null)
            {
                await UpdateBatteryFunc.Invoke();
            }
        }

        partial void OnLastUpdateChanged(DateTimeOffset value)
        {
            Console.WriteLine(ToolTipString);
        }

        public string GetXmlData()
        {
            return
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                $"<xml>" +
                $"<device_id>{XmlEscape(DeviceId)}</device_id>" +
                $"<device_name>{XmlEscape(DeviceName)}</device_name>" +
                $"<device_type>{XmlEscape(DeviceType.ToString())}</device_type>" +
                $"<battery_percent>{BatteryPercentage.ToString("f2", CultureInfo.InvariantCulture)}</battery_percent>" +
                $"<battery_voltage>{BatteryVoltage.ToString("f2", CultureInfo.InvariantCulture)}</battery_voltage>" +
                $"<mileage>{BatteryMileage.ToString("f2", CultureInfo.InvariantCulture)}</mileage>" +
                $"<charging>{PowerSupplyStatus == PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING}</charging>" +
                $"<last_update>{XmlEscape(LastUpdate.ToString("o", CultureInfo.InvariantCulture))}</last_update>" +
                $"</xml>"
                ;
        }

        private static string XmlEscape(string? value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }
    }
}
