using System.Security.Cryptography;
using System.Text;

namespace LGSTrayPrimitives.IPC;

internal static class IpcNameScope
{
    private static readonly string Suffix = CreateSuffix();

    public static string MessagePipeName(string baseName) => $"{baseName}.{Suffix}";

    public static string LocalEventName(string baseName) => $@"Local\{baseName}.{Suffix}";

    private static string CreateSuffix()
    {
        string source = string.Join(
            "|",
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.UserName
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash[..6]).ToLowerInvariant();
    }
}
