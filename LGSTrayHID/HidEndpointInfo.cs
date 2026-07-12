using LGSTrayHID.HidApi;

namespace LGSTrayHID;

internal sealed record HidEndpointInfo(
    string Path,
    Guid ContainerId,
    ushort VendorId,
    ushort ProductId,
    ushort ReleaseNumber,
    string? ManufacturerString,
    string? ProductString,
    string? SerialNumberHash,
    string PathHash,
    string OpenStatus,
    ushort UsagePage,
    ushort Usage,
    int InterfaceNumber,
    HidppMessageType MessageType
)
{
    public string GroupKey => $"{VendorId:X4}:{ContainerId:N}:{ProductId:X4}:{InterfaceNumber}";

    public string ReceiverStableId => !string.IsNullOrWhiteSpace(SerialNumberHash)
        ? $"serial:{SerialNumberHash}"
        : ContainerId != Guid.Empty
            ? $"container:{ContainerId:N}"
            : string.Empty;

    public string PersistentEndpointAlias => $"{ProductId:X4}:{InterfaceNumber}:{PathHash}";

    public string SafeId => $"{ProductId:X4}:{InterfaceNumber}:{UsagePage:X4}:{Usage:X4}:{MessageType}";
}
