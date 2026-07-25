namespace LGSTrayHID;

public sealed record CenturionBridgeNotification(
    byte FeatureIndex,
    byte Function,
    byte[] Data
);

public static class CenturionBridgeNotificationCodec
{
    private const byte MessageEventFunction = 0x10;

    public static bool TryDecode(
        ReadOnlySpan<byte> frame,
        byte expectedReportId,
        byte? expectedDeviceAddress,
        byte expectedBridgeIndex,
        out CenturionBridgeNotification? notification
    )
    {
        notification = null;
        if (!CenturionFrameCodec.TryExtractPayload(
                frame,
                out byte reportId,
                out byte? deviceAddress,
                out byte[] payload
            ) ||
            reportId != expectedReportId ||
            !MatchesAddress(reportId, deviceAddress, expectedDeviceAddress) ||
            payload.Length < 7 ||
            payload[0] != expectedBridgeIndex ||
            payload[1] != MessageEventFunction ||
            (payload[2] & 0xF0) != 0)
        {
            return false;
        }

        int subMessageLength = ((payload[2] & 0x0F) << 8) | payload[3];
        if (subMessageLength < 3 || payload.Length != 4 + subMessageLength)
        {
            return false;
        }

        byte subCpl = payload[4];
        byte subFunctionSw = payload[6];
        if (subCpl is not 0x00 and not 0xFF || (subFunctionSw & 0x0F) != 0)
        {
            return false;
        }

        notification = new CenturionBridgeNotification(
            payload[5],
            (byte)(subFunctionSw >> 4),
            payload[7..]
        );
        return true;
    }

    private static bool MatchesAddress(byte reportId, byte? actualAddress, byte? expectedAddress)
    {
        if (reportId == CenturionFrameCodec.AddressedReportId)
        {
            return expectedAddress.HasValue && actualAddress == expectedAddress;
        }

        return reportId == CenturionFrameCodec.ReportId &&
            actualAddress == null &&
            expectedAddress == null;
    }
}
