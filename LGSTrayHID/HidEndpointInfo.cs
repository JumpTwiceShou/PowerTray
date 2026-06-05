using LGSTrayHID.HidApi;

namespace LGSTrayHID;

internal sealed record HidEndpointInfo(
    string Path,
    Guid ContainerId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InterfaceNumber,
    HidppMessageType MessageType
)
{
    public string GroupKey => $"{ContainerId:N}:{ProductId:X4}:{InterfaceNumber}";
}
