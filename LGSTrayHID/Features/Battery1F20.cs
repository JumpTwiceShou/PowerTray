using LGSTrayPrimitives;
using static LGSTrayPrimitives.PowerSupplyStatus;

namespace LGSTrayHID.Features
{
    public static class Battery1F20
    {
        private static readonly (int Millivolts, int Percent)[] BatteryVoltageToPercentage =
        [
            (4186, 100),
            (4067, 90),
            (3989, 80),
            (3922, 70),
            (3859, 60),
            (3811, 50),
            (3778, 40),
            (3751, 30),
            (3717, 20),
            (3671, 10),
            (3646, 5),
            (3579, 2),
            (3500, 0),
        ];

        public static async Task<BatteryUpdateReturn?> GetBatteryAsync(HidppDevice device)
        {
            Hidpp20 buffer = new byte[7] { 0x10, device.DeviceIdx, device.FeatureMap[0x1F20], 0x00 | HidppDevices.SW_ID, 0x00, 0x00, 0x00 };
            Hidpp20 ret = await device.Parent.WriteRead20(device.Parent.DevShort, buffer);

            if (ret.Length < 7 || ret.GetFeatureIndex() == 0x8F)
            {
                return null;
            }

            return Decode(ret.GetParam(0), ret.GetParam(1), ret.GetParam(2));
        }

        public static BatteryUpdateReturn? Decode(byte millivoltsHigh, byte millivoltsLow, byte flags)
        {
            if ((flags & 0x01) == 0)
            {
                return null;
            }

            int millivolts = (millivoltsHigh << 8) | millivoltsLow;
            double percent = EstimateBatteryLevelPercentage(millivolts);
            PowerSupplyStatus status = (flags & 0x02) != 0
                ? POWER_SUPPLY_STATUS_CHARGING
                : POWER_SUPPLY_STATUS_DISCHARGING;

            return new BatteryUpdateReturn(percent, status, millivolts);
        }

        public static int EstimateBatteryLevelPercentage(int millivolts)
        {
            if (millivolts >= BatteryVoltageToPercentage[0].Millivolts)
            {
                return BatteryVoltageToPercentage[0].Percent;
            }

            if (millivolts <= BatteryVoltageToPercentage[^1].Millivolts)
            {
                return BatteryVoltageToPercentage[^1].Percent;
            }

            for (int i = 0; i < BatteryVoltageToPercentage.Length - 1; i++)
            {
                (int highMv, int highPercent) = BatteryVoltageToPercentage[i];
                (int lowMv, int lowPercent) = BatteryVoltageToPercentage[i + 1];
                if (millivolts < lowMv || millivolts > highMv)
                {
                    continue;
                }

                double percent = lowPercent + (highPercent - lowPercent) * (millivolts - lowMv) / (double)(highMv - lowMv);
                return (int)Math.Round(percent);
            }

            return 0;
        }
    }
}
