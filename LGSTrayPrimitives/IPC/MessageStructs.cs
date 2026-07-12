using LGSTrayPrimitives.IPC;
using MessagePack;

namespace LGSTrayPrimitives.MessageStructs;

public enum IPCMessageType : byte
{
    HEARTBEAT = 0,
    INIT,
    UPDATE,
    OFFLINE,
    NATIVE_DIAGNOSTICS_RESPONSE,
    NATIVE_REDISCOVER_RESPONSE
}

public enum IPCMessageRequestType : byte
{
    BATTERY_UPDATE_REQUEST = 0,
    NATIVE_DIAGNOSTICS_REQUEST,
    NATIVE_REDISCOVER_REQUEST,
    NATIVE_HEALTH_CHECK_REQUEST
}

[Union(0, typeof(InitMessage))]
[Union(1, typeof(UpdateMessage))]
[Union(2, typeof(DeviceOfflineMessage))]
[Union(3, typeof(NativeDiagnosticsResponseMessage))]
[Union(4, typeof(HeartbeatMessage))]
[Union(5, typeof(NativeRediscoverResponseMessage))]
public abstract class IPCMessage(string deviceId)
{
    [Key(0)]
    public string deviceId = deviceId;

    [Key(10)]
    public string nonce = string.Empty;

    [Key(11)]
    public long issuedAtUnixMilliseconds;

    [Key(12)]
    public string authTag = string.Empty;
}

[MessagePackObject]
public class InitMessage(string deviceId, string deviceName, bool hasBattery, DeviceType deviceType) : IPCMessage(deviceId)
{
    [Key(1)]
    public string deviceName = deviceName;

    [Key(2)]
    public bool hasBattery = hasBattery;

    [Key(3)]
    public DeviceType deviceType = deviceType;
}

[MessagePackObject]
public class UpdateMessage(
    string deviceId,
    double batteryPercentage,
    PowerSupplyStatus powerSupplyStatus,
    int batteryMVolt,
    DateTimeOffset updateTime,
    double mileage = -1
) : IPCMessage(deviceId)
{
    [Key(1)]
    public double batteryPercentage = batteryPercentage;

    [Key(2)]
    public PowerSupplyStatus powerSupplyStatus = powerSupplyStatus;

    [Key(3)]
    public int batteryMVolt = batteryMVolt;

    [Key(4)]
    public DateTimeOffset updateTime = updateTime;

    [Key(5)]
    public double Mileage = mileage;
}

[MessagePackObject]
public class DeviceOfflineMessage(string deviceId) : IPCMessage(deviceId)
{
}

[MessagePackObject]
public class NativeDiagnosticsResponseMessage(
    string requestId,
    string diagnosticsJson,
    string summaryText,
    string? error = null
) : IPCMessage("native-diagnostics")
{
    public const string LatestSnapshotRequestId = "latest";

    [Key(1)]
    public string requestId = requestId;

    [Key(2)]
    public string diagnosticsJson = diagnosticsJson;

    [Key(3)]
    public string summaryText = summaryText;

    [Key(4)]
    public string? error = error;
}

[MessagePackObject]
public class NativeRediscoverResponseMessage(string requestId, string? error = null) : IPCMessage("native-rediscover")
{
    [Key(1)]
    public string requestId = requestId;

    [Key(2)]
    public string? error = error;
}

[MessagePackObject]
public class HeartbeatMessage(
    int helperProcessId,
    DateTimeOffset startedAt,
    DateTimeOffset lastSuccessfulCommandAt,
    string state,
    string? lastError = null
) : IPCMessage("native-heartbeat")
{
    public HeartbeatMessage() : this(0, default, default, string.Empty, null)
    {
    }

    [Key(1)]
    public int helperProcessId = helperProcessId;

    [Key(2)]
    public DateTimeOffset startedAt = startedAt;

    [Key(3)]
    public DateTimeOffset lastSuccessfulCommandAt = lastSuccessfulCommandAt;

    [Key(4)]
    public string state = state;

    [Key(5)]
    public string? lastError = lastError;
}

[Union(0, typeof(BatteryUpdateRequestMessage))]
[Union(1, typeof(NativeDiagnosticsRequestMessage))]
[Union(2, typeof(NativeRediscoverRequestMessage))]
[Union(3, typeof(NativeHealthCheckRequestMessage))]
public abstract class IPCRequestMessage(string requestId)
{
    [Key(0)]
    public string requestId = requestId;

    [Key(10)]
    public string nonce = string.Empty;

    [Key(11)]
    public long issuedAtUnixMilliseconds;

    [Key(12)]
    public string authTag = string.Empty;
}

[MessagePackObject]
public class BatteryUpdateRequestMessage() : IPCRequestMessage(Guid.NewGuid().ToString("N"))
{
    [Key(1)]
    public int id;
}

[MessagePackObject]
public class NativeDiagnosticsRequestMessage(string requestId) : IPCRequestMessage(requestId)
{
}

[MessagePackObject]
public class NativeRediscoverRequestMessage(string requestId) : IPCRequestMessage(requestId)
{
}

[MessagePackObject]
public class NativeHealthCheckRequestMessage(string requestId) : IPCRequestMessage(requestId)
{
}
