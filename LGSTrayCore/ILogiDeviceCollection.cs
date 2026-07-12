using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayCore;

public interface ILogiDeviceCollection
{
    IReadOnlyList<LogiDevice> GetDevices();

    void OnInitMessage(InitMessage initMessage);

    void OnUpdateMessage(UpdateMessage updateMessage);
}
