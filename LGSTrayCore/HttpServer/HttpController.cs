using EmbedIO.Routing;
using EmbedIO;
using EmbedIO.WebApi;
using System.Net;
using System.Reflection;

namespace LGSTrayCore.HttpServer;

public class HttpControllerFactory
{
    private readonly ILogiDeviceCollection _logiDeviceCollection;

    public HttpControllerFactory(ILogiDeviceCollection logiDeviceCollection)
    {
        _logiDeviceCollection = logiDeviceCollection;
    }

    public HttpController CreateController()
    {
        return new HttpController(_logiDeviceCollection);
    }
}

public class HttpController : WebApiController
{
    private static readonly string _assemblyVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion!;
    private readonly ILogiDeviceCollection _logiDeviceCollection;

    public HttpController(ILogiDeviceCollection logiDeviceCollection)
    {
        _logiDeviceCollection = logiDeviceCollection;
    }

    private void DefaultResponse(string contentType = "text/html")
    {
        Response.ContentType = contentType;
        Response.DisableCaching();
        Response.KeepAlive = false;
    }

    [Route(HttpVerbs.Get, "/")]
    [Route(HttpVerbs.Get, "/devices")]
    public void GetDevices()
    {
        DefaultResponse();
        LogiDevice[] devices = GetInitializedDevices().ToArray();

        using var tw = HttpContext.OpenResponseText();
        tw.Write("<html>");

        tw.Write("<b>By Device ID</b><br>");
        foreach (var logiDevice in devices)
        {
            string deviceName = WebUtility.HtmlEncode(logiDevice.DeviceName);
            string deviceId = WebUtility.HtmlEncode(logiDevice.DeviceId);
            string deviceIdRoute = Uri.EscapeDataString(logiDevice.DeviceId);
            tw.Write($"{deviceName} : <a href=\"/device/{deviceIdRoute}\">{deviceId}</a><br>");
        }

        tw.Write("<br><b>By Device Name</b><br>");
        foreach (var logiDevice in devices)
        {
            string deviceName = WebUtility.HtmlEncode(logiDevice.DeviceName);
            tw.Write($"<a href=\"/device/{Uri.EscapeDataString(logiDevice.DeviceName)}\">{deviceName}</a><br>");
        }

        tw.Write("<br><hr>");
        tw.Write($"<i>PowerTray version: {_assemblyVersion}</i><br>");
        tw.Write("</html>");

        return;
    }

    [Route(HttpVerbs.Get, "/device/{deviceIden}")]
    public void GetDevice(string deviceIden)
    {
        LogiDevice[] devices = GetInitializedDevices().ToArray();
        var logiDevice = devices.FirstOrDefault(x => x.DeviceId == deviceIden);
        logiDevice ??= devices.FirstOrDefault(x => x.DeviceName == deviceIden);

        using var tw = HttpContext.OpenResponseText();
        if (logiDevice == null)
        {
            DefaultResponse("text/plain");
            HttpContext.Response.StatusCode = 404;
            tw.Write($"{deviceIden} not found.");
            return;
        }

        DefaultResponse("text/xml");

        tw.Write(logiDevice.GetXmlData());
    }

    private IEnumerable<LogiDevice> GetInitializedDevices()
    {
        return _logiDeviceCollection.GetDevices().Where(x =>
            !string.IsNullOrWhiteSpace(x.DeviceId) &&
            x.DeviceId != LogiDevice.NOT_FOUND &&
            x.DeviceName != LogiDevice.NOT_FOUND &&
            x.DeviceName != "Not Initialised"
        );
    }
}
