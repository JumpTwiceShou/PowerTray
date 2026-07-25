namespace LGSTrayHID;

public sealed record CenturionFeatureDescriptor(
    ushort FeatureId,
    byte Index,
    byte Type,
    byte Version
);

public static class CenturionFeatureSetCodec
{
    public static IReadOnlyList<CenturionFeatureDescriptor> DecodeEntries(
        ReadOnlySpan<byte> response,
        byte startIndex
    )
    {
        List<CenturionFeatureDescriptor> features = [];
        if (response.Length >= 5)
        {
            int availableEntries = (response.Length - 1) / 4;
            int entryCount = Math.Min(
                Math.Max(1, (int)response[0]),
                availableEntries
            );
            for (int i = 0; i < entryCount && startIndex + i <= byte.MaxValue; i++)
            {
                int offset = 1 + (i * 4);
                features.Add(new CenturionFeatureDescriptor(
                    (ushort)((response[offset] << 8) | response[offset + 1]),
                    (byte)(startIndex + i),
                    response[offset + 2],
                    response[offset + 3]
                ));
            }

            return features;
        }

        if (response.Length >= 2)
        {
            ushort featureId = response.Length >= 3 && response[0] == 0x00
                ? (ushort)((response[1] << 8) | response[2])
                : (ushort)((response[0] << 8) | response[1]);
            features.Add(new CenturionFeatureDescriptor(featureId, startIndex, 0, 0));
        }

        return features;
    }
}
