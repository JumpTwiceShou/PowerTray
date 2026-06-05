using LGSTrayPrimitives;

namespace LGSTrayHID;

internal static class KnownLogitechDevices
{
    public static string GetDisplayName(string name, DeviceType deviceType, ushort productId = 0)
    {
        return (productId, name, deviceType) switch
        {
            (0x0AF7, _, _) => "PRO X 2 Lightspeed Gaming Headset",
            (_, "PRO X 2 LIGHTSPEED", DeviceType.Headset) => "PRO X 2 Lightspeed Gaming Headset",
            (_, "PRO X2 SUPERSTRIKE", DeviceType.Mouse) => "PRO X2 SUPERSTRIKE Wireless Mouse",
            _ => name,
        };
    }
}
