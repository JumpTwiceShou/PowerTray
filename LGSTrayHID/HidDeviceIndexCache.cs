using System.Collections.Concurrent;

namespace LGSTrayHID;

internal static class HidDeviceIndexCache
{
    private sealed class EndpointIndexes
    {
        public object Sync { get; } = new();
        public byte? DirectIndex { get; set; }
        public HashSet<byte> ReceiverSlots { get; } = [];
    }

    private static readonly ConcurrentDictionary<string, EndpointIndexes> Indexes =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetDirect(HidEndpointInfo endpoint, out byte deviceIndex)
    {
        if (!TryGet(endpoint, out EndpointIndexes indexes))
        {
            deviceIndex = default;
            return false;
        }

        lock (indexes.Sync)
        {
            if (indexes.DirectIndex.HasValue)
            {
                deviceIndex = indexes.DirectIndex.Value;
                return true;
            }
        }

        deviceIndex = default;
        return false;
    }

    public static bool RememberDirect(HidEndpointInfo endpoint, byte deviceIndex)
    {
        if (deviceIndex is not (0x00 or 0xFF) ||
            !TryGetKey(endpoint, out string key))
        {
            return false;
        }

        EndpointIndexes indexes = Indexes.GetOrAdd(key, _ => new EndpointIndexes());
        lock (indexes.Sync)
        {
            indexes.ReceiverSlots.Clear();
            indexes.DirectIndex = deviceIndex;
        }
        return true;
    }

    public static IReadOnlyList<byte> GetReceiverSlots(HidEndpointInfo endpoint)
    {
        if (!TryGet(endpoint, out EndpointIndexes indexes))
        {
            return [];
        }

        lock (indexes.Sync)
        {
            return indexes.ReceiverSlots.Order().ToArray();
        }
    }

    public static bool RememberReceiverSlot(HidEndpointInfo endpoint, byte deviceIndex)
    {
        if (deviceIndex is < 0x01 or > 0x06 ||
            !TryGetKey(endpoint, out string key))
        {
            return false;
        }

        EndpointIndexes indexes = Indexes.GetOrAdd(key, _ => new EndpointIndexes());
        lock (indexes.Sync)
        {
            indexes.DirectIndex = null;
            return indexes.ReceiverSlots.Add(deviceIndex);
        }
    }

    internal static void ClearForTests()
    {
        Indexes.Clear();
    }

    private static bool TryGet(HidEndpointInfo endpoint, out EndpointIndexes indexes)
    {
        if (!TryGetKey(endpoint, out string key))
        {
            indexes = null!;
            return false;
        }

        return Indexes.TryGetValue(key, out indexes!);
    }

    private static bool TryGetKey(HidEndpointInfo endpoint, out string key)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ReceiverStableId))
        {
            key = string.Empty;
            return false;
        }

        key = $"{endpoint.VendorId:X4}:{endpoint.ProductId:X4}:{endpoint.InterfaceNumber}:{endpoint.MessageType}:{endpoint.ReceiverStableId}";
        return true;
    }
}
