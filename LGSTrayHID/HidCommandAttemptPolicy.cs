namespace LGSTrayHID;

internal static class HidCommandAttemptPolicy
{
    private const int DefaultAttempts = 2;
    private const int C54dRecoveryAttempts = 2;

    public static int GetAttempts(bool c54dShortRequest, int? requestedAttempts)
    {
        if (c54dShortRequest)
        {
            return C54dRecoveryAttempts;
        }

        return Math.Clamp(requestedAttempts ?? DefaultAttempts, 1, DefaultAttempts);
    }

    public static bool ShouldUseC54dRecovery(
        bool allowC54dRecovery,
        bool targetsShortEndpoint,
        bool isC54dEndpoint,
        bool hasLongEndpoint,
        int requestLength,
        byte reportId,
        byte deviceIndex
    )
    {
        return allowC54dRecovery
            && targetsShortEndpoint
            && isC54dEndpoint
            && hasLongEndpoint
            && requestLength == 7
            && reportId == 0x10
            && deviceIndex != 0xFF;
    }
}
