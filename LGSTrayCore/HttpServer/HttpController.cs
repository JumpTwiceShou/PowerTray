using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using LGSTrayPrimitives;
using Microsoft.Extensions.Options;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace LGSTrayCore.HttpServer;

public sealed class HttpControllerFactory
{
    private readonly ILogiDeviceCollection _logiDeviceCollection;
    private readonly HttpServerSettings _settings;
    private readonly HttpServerStatus _status;

    public HttpControllerFactory(
        ILogiDeviceCollection logiDeviceCollection,
        IOptions<AppSettings> appSettings,
        HttpServerStatus status
    )
    {
        _logiDeviceCollection = logiDeviceCollection;
        _settings = appSettings.Value.HTTPServer;
        _status = status;
    }

    public HttpController CreateController()
    {
        return new HttpController(_logiDeviceCollection, _settings, _status);
    }
}

public sealed class HttpController : WebApiController
{
    private static readonly string AssemblyVersion =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    private readonly ILogiDeviceCollection _logiDeviceCollection;
    private readonly HttpServerSettings _settings;
    private readonly HttpServerStatus _status;

    public HttpController(
        ILogiDeviceCollection logiDeviceCollection,
        HttpServerSettings settings,
        HttpServerStatus status
    )
    {
        _logiDeviceCollection = logiDeviceCollection;
        _settings = settings;
        _status = status;
    }

    [Route(HttpVerbs.Get, "/")]
    [Route(HttpVerbs.Get, "/devices")]
    public void GetDevices()
    {
        if (!EnsureAuthorized())
        {
            return;
        }

        DefaultResponse();
        LogiDevice[] devices = GetInitializedDevices();
        using TextWriter writer = HttpContext.OpenResponseText();
        writer.Write("<html>");
        writer.Write("<b>By Device ID</b><br>");
        foreach (LogiDevice device in devices)
        {
            string deviceName = WebUtility.HtmlEncode(device.DeviceName);
            string deviceId = WebUtility.HtmlEncode(device.DeviceId);
            string deviceIdRoute = Uri.EscapeDataString(device.DeviceId);
            writer.Write($"{deviceName} : <a href=\"/device/{deviceIdRoute}\">{deviceId}</a><br>");
        }

        writer.Write("<br><b>By Device Name</b><br>");
        foreach (LogiDevice device in devices)
        {
            string deviceName = WebUtility.HtmlEncode(device.DeviceName);
            writer.Write($"<a href=\"/device/{Uri.EscapeDataString(device.DeviceName)}\">{deviceName}</a><br>");
        }

        writer.Write("<br><hr>");
        writer.Write($"<i>PowerTray version: {WebUtility.HtmlEncode(AssemblyVersion)}</i><br>");
        writer.Write("</html>");
    }

    [Route(HttpVerbs.Get, "/device/{deviceIdentifier}")]
    public void GetDevice(string deviceIdentifier)
    {
        if (!EnsureAuthorized())
        {
            return;
        }

        LogiDevice[] devices = GetInitializedDevices();
        LogiDevice? device = devices.FirstOrDefault(item => item.DeviceId == deviceIdentifier)
            ?? devices.FirstOrDefault(item => item.DeviceName == deviceIdentifier);

        using TextWriter writer = HttpContext.OpenResponseText();
        if (device == null)
        {
            DefaultResponse("text/plain; charset=utf-8");
            Response.StatusCode = 404;
            writer.Write($"{deviceIdentifier} not found.");
            return;
        }

        DefaultResponse("application/xml; charset=utf-8");
        writer.Write(device.GetXmlData());
    }

    [Route(HttpVerbs.Get, "/health")]
    public void GetHealth()
    {
        if (!EnsureAuthorized())
        {
            return;
        }

        DefaultResponse("application/json; charset=utf-8");
        HttpServerStatusSnapshot status = _status.Snapshot;
        using TextWriter writer = HttpContext.OpenResponseText();
        writer.Write(JsonSerializer.Serialize(new
        {
            status = status.State,
            startedAt = status.StartedAt,
            restartCount = status.RestartCount,
            deviceCount = GetInitializedDevices().Length,
            version = AssemblyVersion,
        }));
    }

    private bool EnsureAuthorized()
    {
        if (!_settings.RequiresAuthentication)
        {
            return true;
        }

        string? bearer = Request.Headers["Authorization"];
        string? token = null;
        if (!string.IsNullOrWhiteSpace(bearer) && bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = bearer[7..].Trim();
        }
        token ??= Request.Headers["X-PowerTray-Token"];

        if (_settings.IsAuthorized(token))
        {
            return true;
        }

        Response.StatusCode = 401;
        Response.Headers["WWW-Authenticate"] = "Bearer realm=\"PowerTray\"";
        DefaultResponse("text/plain; charset=utf-8");
        using TextWriter writer = HttpContext.OpenResponseText();
        writer.Write("Unauthorized.");
        return false;
    }

    private void DefaultResponse(string contentType = "text/html; charset=utf-8")
    {
        Response.ContentType = contentType;
        Response.DisableCaching();
        Response.KeepAlive = false;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
    }

    private LogiDevice[] GetInitializedDevices()
    {
        return _logiDeviceCollection.GetDevices()
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.DeviceId) &&
                device.DeviceId != LogiDevice.NOT_FOUND &&
                device.DeviceName != LogiDevice.NOT_FOUND &&
                device.DeviceName != "Not Initialised"
            )
            .ToArray();
    }
}
