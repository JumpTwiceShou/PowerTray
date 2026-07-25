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
}
