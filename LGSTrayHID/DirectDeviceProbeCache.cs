using System.Collections.Concurrent;

namespace LGSTrayHID;

internal static class DirectDeviceProbeCache
{
    private static readonly ConcurrentDictionary<string, byte> DeviceIndexes =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(HidEndpointInfo endpoint, out byte deviceIndex)
    {
        string? key = GetKey(endpoint);
        if (key == null)
        {
            deviceIndex = default;
            return false;
        }

        return DeviceIndexes.TryGetValue(key, out deviceIndex);
    }

    public static bool Remember(HidEndpointInfo endpoint, byte deviceIndex)
    {
        string? key = GetKey(endpoint);
        if (key == null || deviceIndex is not (0x00 or 0xFF))
        {
            return false;
        }

        DeviceIndexes[key] = deviceIndex;
        return true;
    }

    internal static void ClearForTests()
    {
        DeviceIndexes.Clear();
    }

    private static string? GetKey(HidEndpointInfo endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ReceiverStableId))
        {
            return null;
        }

        return $"{endpoint.VendorId:X4}:{endpoint.ProductId:X4}:{endpoint.InterfaceNumber}:{endpoint.MessageType}:{endpoint.ReceiverStableId}";
    }
}
