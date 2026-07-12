using System.Security.Cryptography;
using System.Text;

namespace LGSTrayHID;

internal sealed class HidppDeviceIdentity
{
    public string Identifier { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? UnitId { get; init; }
    public string? ModelId { get; init; }
    public bool SerialNumberSupported { get; init; }
    public string? SerialNumber { get; init; }
    public string? DeviceInfoRawResponse { get; init; }
    public string? SerialRawResponse { get; init; }
    public string? FallbackReason { get; init; }

    public static HidppDeviceIdentity FromDeviceInformation(
        string deviceName,
        ushort productId,
        byte deviceIdx,
        int interfaceNumber,
        string receiverStableId,
        string persistentEndpointAlias,
        byte[]? deviceInfoRawResponse,
        byte[]? deviceInfoParams,
        byte[]? serialRawResponse,
        byte[]? serialParams
    )
    {
        string? unitId = null;
        string? modelId = null;
        bool serialNumberSupported = false;

        if (deviceInfoParams is { Length: >= 15 })
        {
            unitId = FormatHex(deviceInfoParams.AsSpan(1, 4));
            modelId = FormatHex(deviceInfoParams.AsSpan(7, Math.Min(6, deviceInfoParams.Length - 7)));
            serialNumberSupported = (deviceInfoParams[14] & 0x01) == 0x01;
        }

        string? serialNumber = serialParams == null ? null : FormatHex(serialParams);
        if (IsMeaningfulHex(serialNumber))
        {
            return Create(
                serialNumber!,
                "deviceInfoSerial",
                unitId,
                modelId,
                serialNumberSupported,
                serialNumber,
                deviceInfoRawResponse,
                serialRawResponse,
                null
            );
        }

        if (IsMeaningfulHex(unitId))
        {
            return Create(
                IsMeaningfulHex(modelId) ? $"{unitId}-{modelId}" : unitId!,
                "deviceInfoUnitId",
                unitId,
                modelId,
                serialNumberSupported,
                IsMeaningfulHex(serialNumber) ? serialNumber : null,
                deviceInfoRawResponse,
                serialRawResponse,
                serialNumberSupported && !IsMeaningfulHex(serialNumber) ? "invalidSerialResponse" : null
            );
        }

        if (!string.IsNullOrWhiteSpace(receiverStableId))
        {
            string pairingKey = string.Join('|',
                "receiver-slot",
                receiverStableId,
                productId.ToString("X4"),
                deviceIdx.ToString("X2"),
                interfaceNumber.ToString(),
                IsMeaningfulHex(modelId) ? modelId : string.Empty
            );
            return Create(
                PersistentDeviceIdentityStore.GetOrCreate(pairingKey),
                "receiverPairingSlot",
                unitId,
                modelId,
                serialNumberSupported,
                IsMeaningfulHex(serialNumber) ? serialNumber : null,
                deviceInfoRawResponse,
                serialRawResponse,
                IsMeaningfulHex(modelId) ? "unitIdMissing" : "deviceInformationIncomplete"
            );
        }

        return CreatePersistentFallback(
            deviceName,
            productId,
            deviceIdx,
            interfaceNumber,
            persistentEndpointAlias,
            modelId,
            serialNumberSupported,
            deviceInfoRawResponse,
            serialRawResponse,
            IsMeaningfulHex(modelId) ? "receiverIdentityMissing" : "deviceInfoMissingOrInvalid"
        );
    }

    public static HidppDeviceIdentity CreateFallback(
        string deviceName,
        ushort productId,
        byte deviceIdx,
        int interfaceNumber,
        string receiverStableId,
        string persistentEndpointAlias,
        byte[]? deviceInfoRawResponse,
        byte[]? serialRawResponse,
        string reason
    )
    {
        if (!string.IsNullOrWhiteSpace(receiverStableId))
        {
            string pairingKey = $"receiver-slot|{receiverStableId}|{productId:X4}|{deviceIdx:X2}|{interfaceNumber}";
            return Create(
                PersistentDeviceIdentityStore.GetOrCreate(pairingKey),
                "receiverPairingSlot",
                null,
                null,
                false,
                null,
                deviceInfoRawResponse,
                serialRawResponse,
                reason
            );
        }

        return CreatePersistentFallback(
            deviceName,
            productId,
            deviceIdx,
            interfaceNumber,
            persistentEndpointAlias,
            null,
            false,
            deviceInfoRawResponse,
            serialRawResponse,
            reason
        );
    }

    public DeviceIdentityDiagnostic ToDiagnostic()
    {
        return new DeviceIdentityDiagnostic
        {
            Source = Source,
            UnitIdHash = HashForDiagnostics(UnitId),
            ModelIdHash = HashForDiagnostics(ModelId),
            SerialNumberSupported = SerialNumberSupported,
            SerialNumberHash = HashForDiagnostics(SerialNumber),
            DeviceInfoRawResponseHash = HashForDiagnostics(DeviceInfoRawResponse),
            SerialRawResponseHash = HashForDiagnostics(SerialRawResponse),
            FallbackReason = FallbackReason,
        };
    }

    internal static bool IsMeaningfulTextIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        string normalized = identifier.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Length > 0 &&
               normalized.Any(character => character != '0') &&
               normalized.Any(character => character != 'F' && character != 'f');
    }

    internal static string CreateStableFallbackIdentifier(string prefix, params string?[] stableParts)
    {
        string source = string.Join("|", stableParts.Select(part => part ?? string.Empty));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{Convert.ToHexString(hash[..6])}";
    }

    private static HidppDeviceIdentity CreatePersistentFallback(
        string deviceName,
        ushort productId,
        byte deviceIdx,
        int interfaceNumber,
        string persistentEndpointAlias,
        string? modelId,
        bool serialNumberSupported,
        byte[]? deviceInfoRawResponse,
        byte[]? serialRawResponse,
        string reason
    )
    {
        string aliasKey = string.Join('|',
            "endpoint-mapping",
            persistentEndpointAlias,
            productId.ToString("X4"),
            deviceIdx.ToString("X2"),
            interfaceNumber.ToString(),
            deviceName,
            IsMeaningfulHex(modelId) ? modelId : string.Empty
        );
        return Create(
            PersistentDeviceIdentityStore.GetOrCreate(aliasKey),
            "persistentEndpointMapping",
            null,
            modelId,
            serialNumberSupported,
            null,
            deviceInfoRawResponse,
            serialRawResponse,
            reason
        );
    }

    private static HidppDeviceIdentity Create(
        string identifier,
        string source,
        string? unitId,
        string? modelId,
        bool serialNumberSupported,
        string? serialNumber,
        byte[]? deviceInfoRawResponse,
        byte[]? serialRawResponse,
        string? fallbackReason
    )
    {
        return new HidppDeviceIdentity
        {
            Identifier = identifier,
            Source = source,
            UnitId = unitId,
            ModelId = modelId,
            SerialNumberSupported = serialNumberSupported,
            SerialNumber = serialNumber,
            DeviceInfoRawResponse = FormatBytes(deviceInfoRawResponse),
            SerialRawResponse = FormatBytes(serialRawResponse),
            FallbackReason = fallbackReason,
        };
    }

    private static bool IsMeaningfulHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Length > 0 &&
               normalized.Any(character => character != '0') &&
               normalized.Any(character => character != 'F' && character != 'f');
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    private static string? FormatBytes(byte[]? bytes) => bytes == null ? null : Convert.ToHexString(bytes);

    private static string? HashForDiagnostics(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
