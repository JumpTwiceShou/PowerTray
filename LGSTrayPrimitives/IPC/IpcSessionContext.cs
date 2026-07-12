using LGSTrayPrimitives.MessageStructs;
using MessagePack;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LGSTrayPrimitives.IPC;

public static class IpcSessionContext
{
    public const string EnvironmentVariableName = "POWERTRAY_IPC_TOKEN";

    private const long MaximumClockSkewMilliseconds = 120_000;
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, long> ConsumedNonces = new(StringComparer.Ordinal);
    private static byte[] _key = [];

    public static bool IsConfigured
    {
        get
        {
            lock (Sync)
            {
                return _key.Length >= 32;
            }
        }
    }

    public static string CreateAndSetToken()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        SetToken(token);
        return token;
    }

    public static void SetToken(string? token)
    {
        string normalized = token?.Trim() ?? string.Empty;
        if (normalized.Length < 32)
        {
            throw new InvalidOperationException("PowerTray IPC token is missing or invalid.");
        }

        byte[] key;
        try
        {
            key = normalized.Length % 2 == 0 && normalized.All(Uri.IsHexDigit)
                ? Convert.FromHexString(normalized)
                : Encoding.UTF8.GetBytes(normalized);
        }
        catch (FormatException)
        {
            key = Encoding.UTF8.GetBytes(normalized);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("PowerTray IPC token must contain at least 256 bits of key material.");
        }

        lock (Sync)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = key;
            ConsumedNonces.Clear();
        }
    }

    public static void Sign(IPCMessageType type, IPCMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (message)
        {
            PrepareEnvelope(message);
            message.authTag = ComputeTag((byte)type, MessagePackSerializer.Serialize<IPCMessage>(message));
        }
    }

    public static void Sign(IPCMessageRequestType type, IPCRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (message)
        {
            PrepareEnvelope(message);
            message.authTag = ComputeTag((byte)type, MessagePackSerializer.Serialize<IPCRequestMessage>(message));
        }
    }

    public static bool Validate(IPCMessageType type, IPCMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (message)
        {
            return ValidateEnvelope(
                (byte)type,
                message.nonce,
                message.issuedAtUnixMilliseconds,
                message.authTag,
                () => MessagePackSerializer.Serialize<IPCMessage>(message),
                value => message.authTag = value
            );
        }
    }

    public static bool Validate(IPCMessageRequestType type, IPCRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (message)
        {
            return ValidateEnvelope(
                (byte)type,
                message.nonce,
                message.issuedAtUnixMilliseconds,
                message.authTag,
                () => MessagePackSerializer.Serialize<IPCRequestMessage>(message),
                value => message.authTag = value
            );
        }
    }

    private static void PrepareEnvelope(IPCMessage message)
    {
        message.nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        message.issuedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        message.authTag = string.Empty;
    }

    private static void PrepareEnvelope(IPCRequestMessage message)
    {
        message.nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        message.issuedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        message.authTag = string.Empty;
    }

    private static bool ValidateEnvelope(
        byte type,
        string? nonce,
        long issuedAtUnixMilliseconds,
        string? authTag,
        Func<byte[]> serializeWithoutTag,
        Action<string> setAuthTag
    )
    {
        if (!IsConfigured || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(authTag) ||
            nonce.Length != 32 || authTag.Length != 64 ||
            !nonce.All(Uri.IsHexDigit) || !authTag.All(Uri.IsHexDigit))
        {
            return false;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(now - issuedAtUnixMilliseconds) > MaximumClockSkewMilliseconds)
        {
            return false;
        }

        string suppliedTag = authTag;
        setAuthTag(string.Empty);
        string expectedTag;
        try
        {
            expectedTag = ComputeTag(type, serializeWithoutTag());
        }
        catch (Exception ex) when (ex is MessagePackSerializationException or InvalidOperationException or CryptographicException)
        {
            return false;
        }
        finally
        {
            setAuthTag(suppliedTag);
        }

        byte[] suppliedBytes;
        byte[] expectedBytes;
        try
        {
            suppliedBytes = Convert.FromHexString(suppliedTag);
            expectedBytes = Convert.FromHexString(expectedTag);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
        {
            return false;
        }

        CleanupExpiredNonces(now);
        return ConsumedNonces.TryAdd($"{type:X2}:{nonce}", issuedAtUnixMilliseconds + MaximumClockSkewMilliseconds);
    }

    private static string ComputeTag(byte type, byte[] serializedMessage)
    {
        byte[] key;
        lock (Sync)
        {
            if (_key.Length < 32)
            {
                throw new InvalidOperationException("PowerTray IPC session is not configured.");
            }
            key = _key.ToArray();
        }

        try
        {
            byte[] authenticatedData = new byte[serializedMessage.Length + 1];
            authenticatedData[0] = type;
            Buffer.BlockCopy(serializedMessage, 0, authenticatedData, 1, serializedMessage.Length);
            return Convert.ToHexString(HMACSHA256.HashData(key, authenticatedData));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void CleanupExpiredNonces(long now)
    {
        if (ConsumedNonces.Count < 1024)
        {
            return;
        }

        foreach ((string nonce, long expiresAt) in ConsumedNonces)
        {
            if (expiresAt < now)
            {
                _ = ConsumedNonces.TryRemove(nonce, out _);
            }
        }
    }
}
