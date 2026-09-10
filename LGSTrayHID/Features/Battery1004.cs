using LGSTrayPrimitives;
using static LGSTrayPrimitives.PowerSupplyStatus;

namespace LGSTrayHID.Features
{
    public static class Battery1004
    {
        public static async Task<BatteryUpdateReturn?> GetBatteryAsync(HidppDevice device)
        {
            Hidpp20 buffer = new byte[7] { 0x10, device.DeviceIdx, device.FeatureMap[0x1004], 0x10 | HidppDevices.SW_ID, 0x00, 0x00, 0x00 };
            Hidpp20 ret = await device.Parent.WriteRead20(device.Parent.DevShort, buffer);

            if (ret.Length < 7 || ret.GetFeatureIndex() == 0x8F) { return null; }

            return Decode(ret.GetParam(0), ret.GetParam(2));
        }

        public static BatteryUpdateReturn Decode(byte batteryPercentage, byte statusByte)
        {
            PowerSupplyStatus status = statusByte switch
            {
                0 => POWER_SUPPLY_STATUS_DISCHARGING,
                1 or 2 => POWER_SUPPLY_STATUS_CHARGING,
                3 => POWER_SUPPLY_STATUS_FULL,
                _ => POWER_SUPPLY_STATUS_NOT_CHARGING,
            };
            return new BatteryUpdateReturn(batteryPercentage, status, -1);
        }

    }
}
