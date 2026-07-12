using Microsoft.Win32.SafeHandles;
using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID.HidApi;

internal sealed class SafeHidDeviceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeHidDeviceHandle() : base(ownsHandle: true)
    {
    }

    internal SafeHidDeviceHandle(nint handle) : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        HidClose(handle);
        return true;
    }
}
