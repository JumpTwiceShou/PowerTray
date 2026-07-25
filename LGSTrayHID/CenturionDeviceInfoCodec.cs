using System.Text;

namespace LGSTrayHID;

public sealed record CenturionHardwareInfo(
    byte ModelId,
    byte HardwareRevision,
    ushort ProductId
);

public sealed record CenturionFirmwareInfo(
    byte Type,
    string Name,
    string Version
);

public static class CenturionDeviceInfoCodec
{
    public static bool TryDecodeHardware(
        ReadOnlySpan<byte> response,
        out CenturionHardwareInfo? hardware
    )
    {
        hardware = null;
        if (response.Length < 4)
        {
            return false;
        }

        hardware = new CenturionHardwareInfo(
            response[0],
            response[1],
            (ushort)((response[2] << 8) | response[3])
        );
        return true;
    }

    public static bool TryDecodeFirmware(
        ReadOnlySpan<byte> response,
        out CenturionFirmwareInfo? firmware
    )
    {
        firmware = null;
        if (response.Length < 5)
        {
            return false;
        }

        int nameLength = response[4];
        if (nameLength > response.Length - 5)
        {
            return false;
        }

        ushort version = (ushort)((response[2] << 8) | response[3]);
        string name = nameLength == 0
            ? string.Empty
            : Encoding.ASCII.GetString(response.Slice(5, nameLength)).TrimEnd('\0');
        firmware = new CenturionFirmwareInfo(
            response[0],
            name,
            $"{version >> 8}.{version & 0xFF:00}"
        );
        return true;
    }
}
