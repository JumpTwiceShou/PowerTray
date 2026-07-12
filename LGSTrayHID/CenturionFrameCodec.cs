namespace LGSTrayHID;

public static class CenturionFrameCodec
{
    public const byte ReportId = 0x51;
    public const byte AddressedReportId = 0x50;
    public const int FrameSize = 64;

    public static byte[] BuildFrame(byte reportId, byte? deviceAddress, ReadOnlySpan<byte> payload)
    {
        ValidateReportId(reportId);
        int payloadOffset = reportId == AddressedReportId ? 4 : 3;
        int maximumPayloadLength = FrameSize - payloadOffset;
        if (payload.Length <= 0 || payload.Length > maximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"Centurion payload must contain 1-{maximumPayloadLength} bytes for report 0x{reportId:X2}."
            );
        }

        byte[] frame = new byte[FrameSize];
        frame[0] = reportId;
        byte cplLength = checked((byte)(payload.Length + 1));
        if (reportId == AddressedReportId)
        {
            frame[1] = deviceAddress ?? throw new ArgumentNullException(nameof(deviceAddress));
            frame[2] = cplLength;
            frame[3] = 0x00;
        }
        else
        {
            if (deviceAddress.HasValue)
            {
                throw new ArgumentException("A device address is only valid for addressed Centurion reports.", nameof(deviceAddress));
            }
            frame[1] = cplLength;
            frame[2] = 0x00;
        }

        payload.CopyTo(frame.AsSpan(payloadOffset, payload.Length));
        return frame;
    }

    public static bool TryExtractPayload(
        ReadOnlySpan<byte> frame,
        out byte reportId,
        out byte? deviceAddress,
        out byte[] payload
    )
    {
        reportId = 0;
        deviceAddress = null;
        payload = [];

        if (frame.Length < 4 || (frame[0] != ReportId && frame[0] != AddressedReportId))
        {
            return false;
        }

        reportId = frame[0];
        int payloadOffset;
        int cplLength;
        if (reportId == AddressedReportId)
        {
            deviceAddress = frame[1];
            cplLength = frame[2];
            payloadOffset = 4;
            if (frame[3] != 0x00)
            {
                return false;
            }
        }
        else
        {
            cplLength = frame[1];
            payloadOffset = 3;
            if (frame[2] != 0x00)
            {
                return false;
            }
        }

        int payloadLength = cplLength - 1;
        if (cplLength < 2 || payloadLength > FrameSize - payloadOffset || payloadLength > frame.Length - payloadOffset)
        {
            return false;
        }

        payload = frame.Slice(payloadOffset, payloadLength).ToArray();
        return true;
    }

    private static void ValidateReportId(byte reportId)
    {
        if (reportId is not ReportId and not AddressedReportId)
        {
            throw new ArgumentOutOfRangeException(nameof(reportId), reportId, "Unsupported Centurion report id.");
        }
    }
}
