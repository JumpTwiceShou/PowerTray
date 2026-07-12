using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LGSTrayHID;

internal static class PersistentDeviceIdentityStore
{
    private static readonly object Sync = new();
    private static Dictionary<string, string>? _mappings;

    public static string GetOrCreate(string aliasKey)
    {
        if (string.IsNullOrWhiteSpace(aliasKey))
        {
            throw new ArgumentException("Identity alias key is required.", nameof(aliasKey));
        }

        string normalizedKey = Hash(aliasKey.Trim());
        lock (Sync)
        {
            Dictionary<string, string> mappings = Load();
            if (mappings.TryGetValue(normalizedKey, out string? existing) && IsValid(existing))
            {
                return existing;
            }

            string identifier = $"persistent-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}";
            mappings[normalizedKey] = identifier;
            Save(mappings);
            return identifier;
        }
    }

    private static Dictionary<string, string> Load()
    {
        if (_mappings != null)
        {
            return _mappings;
        }

        try
        {
            string storePath = GetStorePath();
            if (File.Exists(storePath))
            {
                Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(storePath)
                );
                if (loaded != null)
                {
                    _mappings = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                    return _mappings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            NativeDiagnosticsStore.RecordError($"Persistent identity store load failed: {ex.GetType().Name}");
        }

        _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return _mappings;
    }

    private static void Save(Dictionary<string, string> mappings)
    {
        try
        {
            string storePath = GetStorePath();
            string directory = Path.GetDirectoryName(storePath)!;
            Directory.CreateDirectory(directory);
            string temporaryPath = storePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(mappings, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            File.Move(temporaryPath, storePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NativeDiagnosticsStore.RecordError($"Persistent identity store save failed: {ex.GetType().Name}");
        }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _mappings = null;
        }
    }

    private static string GetStorePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("POWERTRAY_TEST_IDENTITY_STORE");
        return !string.IsNullOrWhiteSpace(overridePath)
            ? Path.GetFullPath(overridePath)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerTray",
                "native-device-identities.json"
            );
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith("persistent-", StringComparison.OrdinalIgnoreCase);
    }
}
