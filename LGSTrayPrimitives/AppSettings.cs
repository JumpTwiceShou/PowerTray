using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace LGSTrayPrimitives;

public class AppSettings
{
    public UISettings UI { get; set; } = new();

    public HttpServerSettings HTTPServer { get; set; } = new();

    public IDeviceManagerSettings GHub { get; set; } = new();

    public NativeDeviceManagerSettings Native { get; set; } = new();
}

public class UISettings
{
    public bool EnableRichToolTips { get; set; }
}

public class HttpServerSettings
{
    private int _port = 12321;
    private string _addr = "localhost";
    private string _accessToken = string.Empty;

    public bool Enabled { get; set; }

    public int Port
    {
        get => _port;
        set => _port = Math.Clamp(value, 1, 65535);
    }

    public bool AllowRemote { get; set; }

    public string Addr
    {
        get => _addr;
        set => _addr = string.IsNullOrWhiteSpace(value) ? "localhost" : value.Trim();
    }

    public string AccessToken
    {
        get => _accessToken;
        set => _accessToken = value?.Trim() ?? string.Empty;
    }

    public bool UseIpv6 { get; set; }

    public bool IsRemoteAccessConfigured => AllowRemote && AccessToken.Length >= 32;

    public bool RequiresAuthentication => !IsLoopbackHost(GetBindAddress());

    public string UrlPrefix => $"http://{GetBindAddress()}:{Port}";

    public bool IsAuthorized(string? suppliedToken)
    {
        if (!RequiresAuthentication)
        {
            return true;
        }

        if (!IsRemoteAccessConfigured || string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(AccessToken);
        byte[] supplied = Encoding.UTF8.GetBytes(suppliedToken.Trim());
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private string GetBindAddress()
    {
        if (!IsRemoteAccessConfigured || !AllowRemote)
        {
            return IsLoopbackHost(_addr) ? NormalizeLoopback(_addr) : "localhost";
        }

        if (_addr == "0.0.0.0")
        {
            return "+";
        }

        string normalized = _addr.Trim().Trim('[', ']');
        return IPAddress.TryParse(normalized, out IPAddress? address) &&
               address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{normalized}]"
            : _addr;
    }

    private static string NormalizeLoopback(string host)
    {
        string normalized = host.Trim();
        return normalized.Equals("::1", StringComparison.OrdinalIgnoreCase) ? "[::1]" : normalized;
    }

    private static bool IsLoopbackHost(string host)
    {
        string normalized = host.Trim().Trim('[', ']');
        return normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}

public class IDeviceManagerSettings
{
    public bool Enabled { get; set; }
}

public class NativeDeviceManagerSettings : IDeviceManagerSettings
{
    private int _retryTime = 10;
    private int _pollPeriod = 600;
    private int _presencePeriod = 60;
    private int _consecutiveFailureThreshold = 3;
    private string[] _disabledDevices = [];

    public int RetryTime
    {
        get => _retryTime;
        set => _retryTime = Math.Clamp(value, 1, 300);
    }

    public int PollPeriod
    {
        get => _pollPeriod;
        set => _pollPeriod = Math.Clamp(value, 30, 86400);
    }

    public int PresencePeriod
    {
        get => _presencePeriod;
        set => _presencePeriod = Math.Clamp(value, 15, 600);
    }

    public int ConsecutiveFailureThreshold
    {
        get => _consecutiveFailureThreshold;
        set => _consecutiveFailureThreshold = Math.Clamp(value, 2, 10);
    }

    public IEnumerable<string> DisabledDevices
    {
        get => _disabledDevices;
        set => _disabledDevices = (value ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
