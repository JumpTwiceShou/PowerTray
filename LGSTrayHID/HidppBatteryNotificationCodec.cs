using LGSTrayHID.Features;
using LGSTrayPrimitives;

namespace LGSTrayHID;

internal readonly record struct HidppBatteryNotification(
    ushort FeatureId,
    BatteryUpdateReturn? Battery
);

internal static class HidppBatteryNotificationCodec
{
    private const byte HidppShortReportId = 0x10;
    private const byte HidppLongReportId = 0x11;
    private const byte BatteryEventFunctionAndSoftwareId = 0x00;

    public static bool TryDecode(
        ReadOnlySpan<byte> frame,
        byte expectedDeviceIndex,
        ushort selectedFeatureId,
        byte selectedFeatureIndex,
        out HidppBatteryNotification notification
    )
    {
        notification = default;
        if (frame.Length < 7 ||
            (frame[0] != HidppShortReportId && frame[0] != HidppLongReportId) ||
            frame[1] != expectedDeviceIndex ||
            frame[2] != selectedFeatureIndex ||
            frame[3] != BatteryEventFunctionAndSoftwareId)
        {
            return false;
        }

        BatteryUpdateReturn? battery = selectedFeatureId switch
        {
            0x1000 => Battery1000.Decode(frame[4], frame[6]),
            0x1001 => Battery1001.Decode(frame[4], frame[5], frame[6]),
            0x1004 => Battery1004.Decode(frame[4], frame[6]),
            0x1F20 => Battery1F20.Decode(frame[4], frame[5], frame[6]),
            _ => null,
        };

        if (selectedFeatureId is not (0x1000 or 0x1001 or 0x1004 or 0x1F20))
        {
            return false;
        }

        notification = new HidppBatteryNotification(selectedFeatureId, battery);
        return true;
    }

    public static BatteryUpdateReturn PreserveKnownPercentage(
        ushort? selectedFeatureId,
        BatteryUpdateReturn current,
        BatteryUpdateReturn? previous
    )
    {
        if (selectedFeatureId != 0x1000 ||
            current.batteryPercentage != 0 ||
            current.status is PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING or
                PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL ||
            !previous.HasValue)
        {
            return current;
        }

        return new BatteryUpdateReturn(
            previous.Value.batteryPercentage,
            current.status,
            current.batteryMVolt
        );
    }
}
