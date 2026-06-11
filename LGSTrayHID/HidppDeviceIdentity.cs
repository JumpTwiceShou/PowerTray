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
        string endpointIdentityKey,
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
            return new HidppDeviceIdentity
            {
                Identifier = serialNumber!,
                Source = "deviceInfoSerial",
                UnitId = unitId,
                ModelId = modelId,
                SerialNumberSupported = serialNumberSupported,
                SerialNumber = serialNumber,
                DeviceInfoRawResponse = FormatBytes(deviceInfoRawResponse),
                SerialRawResponse = FormatBytes(serialRawResponse),
            };
        }

        if (IsMeaningfulHex(unitId))
        {
            return new HidppDeviceIdentity
            {
                Identifier = IsMeaningfulHex(modelId) ? $"{unitId}-{modelId}" : unitId!,
                Source = "deviceInfoUnitId",
                UnitId = unitId,
                ModelId = modelId,
                SerialNumberSupported = serialNumberSupported,
                SerialNumber = IsMeaningfulHex(serialNumber) ? serialNumber : null,
                DeviceInfoRawResponse = FormatBytes(deviceInfoRawResponse),
                SerialRawResponse = FormatBytes(serialRawResponse),
                FallbackReason = serialNumberSupported && !IsMeaningfulHex(serialNumber) ? "invalidSerialResponse" : null,
            };
        }

        if (IsMeaningfulHex(modelId))
        {
            return new HidppDeviceIdentity
            {
                Identifier = $"{productId:X4}-{modelId}",
                Source = "deviceInfoModelId",
                UnitId = unitId,
                ModelId = modelId,
                SerialNumberSupported = serialNumberSupported,
                SerialNumber = IsMeaningfulHex(serialNumber) ? serialNumber : null,
                DeviceInfoRawResponse = FormatBytes(deviceInfoRawResponse),
                SerialRawResponse = FormatBytes(serialRawResponse),
                FallbackReason = "unitIdMissing",
            };
        }

        return CreateFallback(
            deviceName,
            productId,
            deviceIdx,
            interfaceNumber,
            endpointIdentityKey,
            deviceInfoRawResponse,
            serialRawResponse,
            "deviceInfoMissingOrInvalid"
        );
    }

    public static HidppDeviceIdentity CreateFallback(
        string deviceName,
        ushort productId,
        byte deviceIdx,
        int interfaceNumber,
        string endpointIdentityKey,
        byte[]? deviceInfoRawResponse,
        byte[]? serialRawResponse,
        string reason
    )
    {
        return new HidppDeviceIdentity
        {
            Identifier = CreateStableFallbackIdentifier(
                $"fallback-{productId:X4}-{deviceIdx:X2}",
                productId.ToString("X4"),
                deviceIdx.ToString("X2"),
                interfaceNumber.ToString(),
                deviceName,
                endpointIdentityKey
            ),
            Source = "fallbackStableHash",
            DeviceInfoRawResponse = FormatBytes(deviceInfoRawResponse),
            SerialRawResponse = FormatBytes(serialRawResponse),
            FallbackReason = reason,
        };
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
        return normalized.Length > 0
            && normalized.Any(x => x != '0')
            && normalized.Any(x => x != 'F' && x != 'f');
    }

    internal static string CreateStableFallbackIdentifier(string prefix, params string?[] stableParts)
    {
        string source = string.Join("|", stableParts.Select(x => x ?? string.Empty));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{Convert.ToHexString(hash[..6])}";
    }

    private static bool IsMeaningfulHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Length > 0
            && normalized.Any(x => x != '0')
            && normalized.Any(x => x != 'F' && x != 'f');
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
