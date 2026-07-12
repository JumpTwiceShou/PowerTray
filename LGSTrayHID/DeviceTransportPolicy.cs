namespace LGSTrayHID;

internal static class DeviceTransportPolicy
{
    public static bool ShouldSignalOffline(int consecutiveFailures, int threshold)
    {
        return consecutiveFailures >= Math.Max(2, threshold);
    }

    public static bool ShouldPublishUpdate<T>(bool forceUpdate, bool wasOffline, T current, T previous)
        where T : IEquatable<T>
    {
        return forceUpdate || wasOffline || !current.Equals(previous);
    }
}
