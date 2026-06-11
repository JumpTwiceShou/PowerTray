namespace LGSTrayPrimitives;

public class AppSettings
{
    public UISettings UI { get; set; } = null!;

    public HttpServerSettings HTTPServer { get; set; } = null!;

    public IDeviceManagerSettings GHub { get; set; } = null!;

    public NativeDeviceManagerSettings Native { get; set; } = null!;
}

public class UISettings
{
    public bool EnableRichToolTips { get; set; }
}

public class HttpServerSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; }
    public bool AllowRemote { get; set; }

    private string _addr = "localhost";
    public string Addr
    {
        get => _addr;
        set => _addr = string.IsNullOrWhiteSpace(value) ? "localhost" : value.Trim();
    }

    public bool UseIpv6 { get; set; }

    public string UrlPrefix => $"http://{GetBindAddress()}:{Port}";

    private string GetBindAddress()
    {
        if (!AllowRemote && !IsLoopbackHost(_addr))
        {
            return "localhost";
        }

        return AllowRemote && _addr == "0.0.0.0" ? "+" : _addr;
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
    public int RetryTime { get; set; } = 10;
    public int PollPeriod { get; set; } = 600;

    public IEnumerable<string> DisabledDevices { get; set; } = [];
}
