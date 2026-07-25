namespace LGSTrayHID;

internal static class HidHotplugRegistrationPolicy
{
    public static bool IsAvailable(int returnCode, int callbackHandle)
    {
        return returnCode == 0 && callbackHandle != 0;
    }
}
