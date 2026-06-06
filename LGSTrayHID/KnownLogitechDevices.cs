using LGSTrayPrimitives;

namespace LGSTrayHID;

internal static class KnownLogitechDevices
{
    private static readonly Dictionary<ushort, string> HeadsetProductNames = new()
    {
        [0x0A66] = "G533 Gaming Headset",
        [0x0A87] = "G935 Gaming Headset",
        [0x0AB5] = "G733 Gaming Headset",
        [0x0ABA] = "PRO X Wireless Gaming Headset",
        [0x0AC4] = "G535 Gaming Headset",
        [0x0AFE] = "G733 Gaming Headset",
        [0x0AF7] = "PRO X 2 Lightspeed Gaming Headset",
        [0x0B18] = "G522 LIGHTSPEED Gaming Headset",
        [0x0B19] = "G522 LIGHTSPEED Gaming Headset",
    };

    public static string GetDisplayName(string name, DeviceType deviceType, ushort productId = 0)
    {
        return (productId, name, deviceType) switch
        {
            (0x0AF7, _, _) => "PRO X 2 Lightspeed Gaming Headset",
            (0x0B18 or 0x0B19, _, _) => "G522 LIGHTSPEED Gaming Headset",
            (0x0AB5 or 0x0AFE, _, _) => "G733 Gaming Headset",
            (_, "PRO X 2 LIGHTSPEED", DeviceType.Headset) => "PRO X 2 Lightspeed Gaming Headset",
            (_, "PRO X2 SUPERSTRIKE", DeviceType.Mouse) => "PRO X2 SUPERSTRIKE Wireless Mouse",
            _ => name,
        };
    }

    public static DeviceType GetDeviceType(string name, DeviceType detectedType, ushort productId = 0)
    {
        if (HeadsetProductNames.ContainsKey(productId))
        {
            return DeviceType.Headset;
        }

        return name.Contains("Headset", StringComparison.OrdinalIgnoreCase)
            ? DeviceType.Headset
            : detectedType;
    }

    public static bool IsCenturionProduct(ushort productId)
    {
        return productId is 0x0AF7 or 0x0B18 or 0x0B19;
    }

    public static byte GetCenturionReportId(ushort productId)
    {
        return productId is 0x0B18 or 0x0B19 ? (byte)0x50 : (byte)0x51;
    }

    public static string GetFallbackName(DeviceType deviceType, ushort productId)
    {
        return HeadsetProductNames.TryGetValue(productId, out string? name)
            ? name
            : deviceType switch
            {
                DeviceType.Headset => "Logitech Headset",
                DeviceType.Keyboard => "Logitech Keyboard",
                _ => "Logitech Device",
            };
    }
}
