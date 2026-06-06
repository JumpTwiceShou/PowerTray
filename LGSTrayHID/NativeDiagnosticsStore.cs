using LGSTrayHID.HidApi;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LGSTrayHID;

internal sealed class NativeDiagnosticsSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public List<HidEndpointDiagnostic> HidEnumeration { get; set; } = [];
    public List<HidEndpointDiagnostic> UnsupportedHidDevices { get; set; } = [];
    public List<DiscoverySessionDiagnostic> NativeDiscovery { get; set; } = [];
    public List<NativeDiagnosticEvent> RecentEvents { get; set; } = [];
}

internal sealed class HidEndpointDiagnostic
{
    public string VendorId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string ReleaseNumber { get; set; } = string.Empty;
    public string? ManufacturerString { get; set; }
    public string? ProductString { get; set; }
    public string? SerialNumberHash { get; set; }
    public string PathHash { get; set; } = string.Empty;
    public string UsagePage { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public int InterfaceNumber { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string HidppMessageType { get; set; } = string.Empty;
    public string OpenStatus { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
}

internal sealed class DiscoverySessionDiagnostic
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProductId { get; set; } = string.Empty;
    public string SelectedShortEndpoint { get; set; } = string.Empty;
    public string? SelectedLongEndpoint { get; set; }
    public List<string> FailureReasons { get; set; } = [];
    public string? ReceiverDiscoveryResponse { get; set; }
    public Dictionary<string, bool> PingResults { get; set; } = [];
    public List<DeviceDiscoveryDiagnostic> Devices { get; set; } = [];
    public CenturionDiscoveryDiagnostic? Centurion { get; set; }
}

internal sealed class DeviceDiscoveryDiagnostic
{
    public string DeviceIndex { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    public string? Identifier { get; set; }
    public DeviceIdentityDiagnostic? Identity { get; set; }
    public Dictionary<string, string> FeatureMap { get; set; } = [];
    public string? SelectedBatteryFeature { get; set; }
    public string? LastBatteryResponse { get; set; }
}

internal sealed class DeviceIdentityDiagnostic
{
    public string Source { get; set; } = string.Empty;
    public string? UnitId { get; set; }
    public string? ModelId { get; set; }
    public bool SerialNumberSupported { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceInfoRawResponse { get; set; }
    public string? SerialRawResponse { get; set; }
    public string? FallbackReason { get; set; }
}

internal sealed class CenturionDiscoveryDiagnostic
{
    public string ReportId { get; set; } = string.Empty;
    public string? DeviceAddress { get; set; }
    public int ProbeAttempts { get; set; }
    public Dictionary<string, string> DongleFeatureMap { get; set; } = [];
    public string? BridgeIndex { get; set; }
    public Dictionary<string, string> SubDeviceFeatureMap { get; set; } = [];
    public string? BatteryRawResponse { get; set; }
}

internal sealed class NativeDiagnosticEvent
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public string Message { get; set; } = string.Empty;
}

internal static class NativeDiagnosticsStore
{
    private const int MaxEvents = 200;
    private static readonly object Sync = new();
    private static NativeDiagnosticsSnapshot Current = new();
    private static readonly Queue<NativeDiagnosticEvent> Events = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void BeginDiscovery(IReadOnlyCollection<HidEndpointInfo> endpoints)
    {
        lock (Sync)
        {
            Current = new NativeDiagnosticsSnapshot
            {
                GeneratedAt = DateTimeOffset.Now,
                HidEnumeration = endpoints
                    .Where(x => x.VendorId == 0x046D)
                    .Select(CreateEndpointDiagnostic)
                    .ToList(),
                UnsupportedHidDevices = endpoints
                    .Where(x => x.VendorId != 0x046D || x.MessageType == HidppMessageType.NONE || !x.OpenStatus.Equals("opened", StringComparison.OrdinalIgnoreCase))
                    .Select(CreateEndpointDiagnostic)
                    .ToList(),
                RecentEvents = Events.ToList(),
            };
        }

        AddEvent($"Rediscover started; endpoints={endpoints.Count}");
    }

    public static DiscoverySessionDiagnostic AddSession(HidEndpointInfo shortEndpoint, HidEndpointInfo? longEndpoint)
    {
        DiscoverySessionDiagnostic session = new()
        {
            ProductId = FormatHex(shortEndpoint.ProductId, 4),
            SelectedShortEndpoint = shortEndpoint.SafeId,
            SelectedLongEndpoint = longEndpoint?.SafeId,
        };

        lock (Sync)
        {
            Current.NativeDiscovery.Add(session);
        }

        AddEvent($"Session created for product 0x{shortEndpoint.ProductId:X4}");
        return session;
    }

    public static void UpdateSession(DiscoverySessionDiagnostic session, Action<DiscoverySessionDiagnostic> update)
    {
        lock (Sync)
        {
            update(session);
            Current.RecentEvents = Events.ToList();
        }
    }

    public static void AddEvent(string message)
    {
        lock (Sync)
        {
            Events.Enqueue(new NativeDiagnosticEvent { Message = message });
            while (Events.Count > MaxEvents)
            {
                _ = Events.Dequeue();
            }

            Current.RecentEvents = Events.ToList();
        }
    }

    public static string GetJson()
    {
        lock (Sync)
        {
            Current.RecentEvents = Events.ToList();
            return JsonSerializer.Serialize(Current, JsonOptions);
        }
    }

    public static string GetSummary()
    {
        lock (Sync)
        {
            int recognizedDevices = Current.NativeDiscovery.Sum(x => x.Devices.Count);
            int failedSessions = Current.NativeDiscovery.Count(x => x.FailureReasons.Count > 0);
            return $"Logitech HID endpoints: {Current.HidEnumeration.Count}; unsupported HID endpoints: {Current.UnsupportedHidDevices.Count}; sessions: {Current.NativeDiscovery.Count}; devices: {recognizedDevices}; sessions with failures: {failedSessions}";
        }
    }

    public static string HashForDiagnostics(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    internal static Dictionary<string, string> FormatFeatureMap(IReadOnlyDictionary<ushort, byte> features)
    {
        return features
            .OrderBy(x => x.Value)
            .ToDictionary(x => FormatHex(x.Key, 4), x => FormatHex(x.Value, 2));
    }

    internal static string FormatBytes(byte[]? bytes)
    {
        return bytes == null ? string.Empty : Convert.ToHexString(bytes);
    }

    internal static string FormatHex(int value, int digits)
    {
        return $"0x{value.ToString($"X{digits}")}";
    }

    private static HidEndpointDiagnostic CreateEndpointDiagnostic(HidEndpointInfo endpoint)
    {
        return new HidEndpointDiagnostic
        {
            VendorId = FormatHex(endpoint.VendorId, 4),
            ProductId = FormatHex(endpoint.ProductId, 4),
            ReleaseNumber = FormatHex(endpoint.ReleaseNumber, 4),
            ManufacturerString = endpoint.ManufacturerString,
            ProductString = endpoint.ProductString,
            SerialNumberHash = endpoint.SerialNumberHash,
            PathHash = endpoint.PathHash,
            UsagePage = FormatHex(endpoint.UsagePage, 4),
            Usage = FormatHex(endpoint.Usage, 4),
            InterfaceNumber = endpoint.InterfaceNumber,
            ContainerId = endpoint.ContainerId == Guid.Empty ? string.Empty : endpoint.ContainerId.ToString("N"),
            HidppMessageType = endpoint.MessageType.ToString().ToLowerInvariant(),
            OpenStatus = endpoint.OpenStatus,
            GroupKey = endpoint.GroupKey,
        };
    }
}
