using LGSTrayHID.Features;
using LGSTrayPrimitives;

namespace LGSTrayHID;

public static class CenturionBatteryCodec
{
    public static BatteryUpdateReturn? Decode(ReadOnlySpan<byte> response)
    {
        if (response.Length == 0)
        {
            return null;
        }

        double batteryPercentage = response[0];
        PowerSupplyStatus status = response.Length >= 3
            ? response[2] switch
            {
                1 or 2 => PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING,
                3 => PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL,
                _ => PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING,
            }
            : PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING;

        return new BatteryUpdateReturn(batteryPercentage, status, 0);
    }
}
