namespace LGSTrayHID;

public enum CenturionConnectionState
{
    Disconnected,
    Connected,
}

public static class CenturionConnectionNotificationCodec
{
    private const int ConnectionPayloadLength = 4;

    public static bool TryDecode(
        ReadOnlySpan<byte> frame,
        byte expectedReportId,
        byte? expectedDeviceAddress,
        byte expectedBridgeIndex,
        out CenturionConnectionState state
    )
    {
        state = default;
        if (!CenturionFrameCodec.TryExtractPayload(
                frame,
                out byte reportId,
                out byte? deviceAddress,
                out byte[] payload
            ) ||
            reportId != expectedReportId ||
            !MatchesAddress(reportId, deviceAddress, expectedDeviceAddress) ||
            payload.Length != ConnectionPayloadLength ||
            payload[0] != expectedBridgeIndex ||
            payload[1] != 0x00)
        {
            return false;
        }

        int descriptorListLength = ((payload[2] & 0x0F) << 8) | payload[3];
        state = descriptorListLength == 0
            ? CenturionConnectionState.Disconnected
            : CenturionConnectionState.Connected;
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
