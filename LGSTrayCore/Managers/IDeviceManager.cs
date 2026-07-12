namespace LGSTrayCore.Managers;

public interface IDeviceManager
{
    Task RediscoverDevicesAsync(CancellationToken cancellationToken);

    Task CheckHealthAsync(CancellationToken cancellationToken);
}
