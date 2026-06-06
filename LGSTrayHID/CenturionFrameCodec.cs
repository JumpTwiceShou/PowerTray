namespace LGSTrayHID;

public static class CenturionFrameCodec
{
    public const byte ReportId = 0x51;
    public const byte AddressedReportId = 0x50;
    public const int FrameSize = 64;

    public static byte[] BuildFrame(byte reportId, byte? deviceAddress, ReadOnlySpan<byte> payload)
    {
        if (reportId is not ReportId and not AddressedReportId)
        {
            throw new ArgumentOutOfRangeException(nameof(reportId), reportId, "Unsupported Centurion report id.");
        }

        byte cplLength = checked((byte)(payload.Length + 1));
        byte[] frame = new byte[FrameSize];
        frame[0] = reportId;
        int payloadOffset;
        if (reportId == AddressedReportId)
        {
            frame[1] = deviceAddress ?? 0x00;
            frame[2] = cplLength;
            frame[3] = 0x00;
            payloadOffset = 4;
        }
        else
        {
            frame[1] = cplLength;
            frame[2] = 0x00;
            payloadOffset = 3;
        }

        payload[..Math.Min(payload.Length, frame.Length - payloadOffset)].CopyTo(frame.AsSpan(payloadOffset));
        return frame;
    }

    public static bool TryExtractPayload(ReadOnlySpan<byte> frame, out byte reportId, out byte? deviceAddress, out byte[] payload)
    {
        reportId = 0;
        deviceAddress = null;
        payload = [];

        if (frame.Length < 4 || (frame[0] != ReportId && frame[0] != AddressedReportId))
        {
            return false;
        }

        reportId = frame[0];
        int cplLength;
        int payloadOffset;
        if (reportId == AddressedReportId)
        {
            deviceAddress = frame[1];
            cplLength = frame[2];
            payloadOffset = 4;
        }
        else
        {
            cplLength = frame[1];
            payloadOffset = 3;
        }

        if (cplLength < 1)
        {
            return false;
        }

        int payloadLength = Math.Min(cplLength - 1, frame.Length - payloadOffset);
        if (payloadLength <= 0)
        {
            return false;
        }

        payload = frame[payloadOffset..(payloadOffset + payloadLength)].ToArray();
        return true;
    }
}
