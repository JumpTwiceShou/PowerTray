namespace LGSTrayHID;

internal static class HidSessionRecoveryPolicy
{
    public static bool ShouldBypassOfflineDeferral(
        bool receiverDetected,
        IEnumerable<ushort> deviceIndexes
    )
    {
        return !receiverDetected &&
               deviceIndexes.Any(deviceIndex => deviceIndex is 0x00 or 0xFF);
    }

    public static bool ShouldEmitBypassedOffline(
        bool wasAlreadySignalled,
        bool deferredSignalCancelled
    )
    {
        return !wasAlreadySignalled || deferredSignalCancelled;
    }
}
