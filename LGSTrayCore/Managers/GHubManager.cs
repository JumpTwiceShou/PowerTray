using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Websocket.Client;

namespace LGSTrayCore.Managers;

file struct GHUBMsg
{
    public string MsgId { get; set; }
    public string Verb { get; set; }
    public string Path { get; set; }
    public string Origin { get; set; }
    public JObject Result { get; set; }
    public JObject Payload { get; set; }

    public static bool TryDeserialize(string? json, out GHUBMsg message, out string? error)
    {
        message = default;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty G Hub response.";
            return false;
        }

        try
        {
            GHUBMsg? parsed = JsonConvert.DeserializeObject<GHUBMsg>(json);
            if (!parsed.HasValue)
            {
                error = "G Hub response deserialized to null.";
                return false;
            }

            message = parsed.Value;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}

public partial class GHubManager : IDeviceManager, IHostedService, IDisposable, IAsyncDisposable
{
    private const string WebsocketServer = "ws://localhost:9010";
    private const int GHubPort = 9010;

    [GeneratedRegex(@"\/battery\/dev[0-9a-zA-Z]+\/state")]
    private static partial Regex BatteryDeviceStateRegex();

    private readonly IPublisher<IPCMessage> _deviceEventBus;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _deviceSync = new();
    private readonly object _taskSync = new();
    private readonly HashSet<string> _knownDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _backgroundTasks = [];

    private WebsocketClient? _ws;
    private IDisposable? _messageSubscription;
    private IDisposable? _reconnectionSubscription;
    private bool _disposed;

    public GHubManager(IPublisher<IPCMessage> deviceEventBus)
    {
        _deviceEventBus = deviceEventBus;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await ConnectAsync(linked.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts.Cancel();
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            DisposeConnection();
        }
        finally
        {
            _connectionLock.Release();
        }

        Task[] backgroundTasks;
        lock (_taskSync)
        {
            backgroundTasks = _backgroundTasks.ToArray();
        }
        if (backgroundTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(backgroundTasks).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"G Hub background task shutdown failed: {ex.GetBaseException()}");
            }
        }
    }

    public Task RediscoverDevicesAsync(CancellationToken cancellationToken)
    {
        return RestartAsync(cancellationToken);
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        if (_ws == null)
        {
            await ConnectAsync(cancellationToken);
            return;
        }

        if (!await IsTrustedEndpointOwnerAsync(cancellationToken))
        {
            Debug.WriteLine("G Hub port owner validation failed during health check; disconnecting.");
            await RestartAsync(cancellationToken);
            return;
        }

        LoadDevices();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        DisposeConnection();
        _lifetimeCts.Dispose();
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        try
        {
            await StopAsync(timeout.Token);
        }
        finally
        {
            Dispose();
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            DisposeConnection();
            if (!await IsTrustedEndpointOwnerAsync(cancellationToken))
            {
                Debug.WriteLine("Refusing G Hub connection because localhost:9010 is not owned by a trusted LGHUB_agent process.");
                SignalKnownDevicesOffline("untrustedEndpointOwner");
                return;
            }

            Uri url = new(WebsocketServer);
            WebsocketClient client = new(url, CreateWebSocketClient)
            {
                ErrorReconnectTimeout = TimeSpan.FromMilliseconds(500),
                ReconnectTimeout = null,
            };
            _messageSubscription = client.MessageReceived.Subscribe(ParseSocketMsg);
            _reconnectionSubscription = client.ReconnectionHappened.Subscribe(info =>
            {
                if (info.Type != ReconnectionType.Initial)
                {
                    TrackBackgroundTask(
                        HandleReconnectedAsync(client, _lifetimeCts.Token),
                        "G Hub reconnect recovery"
                    );
                }
            });
            _ws = client;

            Debug.WriteLine($"Trying to connect to trusted LGHUB_agent at {url}");
            try
            {
                await client.Start();
            }
            catch (Exception ex) when (ex is Websocket.Client.Exceptions.WebsocketException or WebSocketException)
            {
                Debug.WriteLine($"Failed to connect to LGHUB_agent: {ex.Message}");
                DisposeConnection();
                SignalKnownDevicesOffline("connectionFailed");
                return;
            }

            if (!await IsTrustedEndpointOwnerAsync(cancellationToken))
            {
                Debug.WriteLine("G Hub port owner changed during connection; disconnecting.");
                DisposeConnection();
                SignalKnownDevicesOffline("endpointOwnerChanged");
                return;
            }

            await RestoreSubscriptionsAsync(client, cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to restart G Hub integration: {ex}");
        }
    }

    private async Task HandleReconnectedAsync(WebsocketClient client, CancellationToken cancellationToken)
    {
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (_disposed || !ReferenceEquals(_ws, client))
                {
                    return;
                }

                await RestoreSubscriptionsAsync(client, cancellationToken);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to restore G Hub subscriptions after reconnect: {ex}");
            SignalKnownDevicesOffline("reconnectRestoreFailed");
        }
    }

    private async Task RestoreSubscriptionsAsync(WebsocketClient client, CancellationToken cancellationToken)
    {
        if (!await IsTrustedEndpointOwnerAsync(cancellationToken))
        {
            throw new InvalidOperationException("G Hub endpoint owner validation failed after connection.");
        }

        if (!ReferenceEquals(_ws, client))
        {
            return;
        }

        SendRequest("SUBSCRIBE", "/devices/state/changed");
        SendRequest("SUBSCRIBE", "/battery/state/changed");
        LoadDevices();
    }

    private static ClientWebSocket CreateWebSocketClient()
    {
        ClientWebSocket client = new();
        client.Options.UseDefaultCredentials = false;
        client.Options.SetRequestHeader("Origin", "file://");
        client.Options.SetRequestHeader("Pragma", "no-cache");
        client.Options.SetRequestHeader("Cache-Control", "no-cache");
        client.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");
        client.Options.SetRequestHeader("Sec-WebSocket-Protocol", "json");
        client.Options.AddSubProtocol("json");
        return client;
    }

    private void DisposeConnection()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;
        _reconnectionSubscription?.Dispose();
        _reconnectionSubscription = null;
        _ws?.Dispose();
        _ws = null;
    }

    public void LoadDevices()
    {
        SendRequest("GET", "/devices/list");
    }

    private void SendRequest(string verb, string path)
    {
        try
        {
            _ws?.Send(JsonConvert.SerializeObject(new
            {
                msgId = Guid.NewGuid().ToString("N"),
                verb,
                path,
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send G Hub request {verb} {path}: {ex.Message}");
        }
    }

    private void ParseSocketMsg(ResponseMessage msg)
    {
        if (!GHUBMsg.TryDeserialize(msg.Text, out GHUBMsg ghubMessage, out string? error))
        {
            Debug.WriteLine($"Failed to parse G Hub message: {error}");
            return;
        }

        try
        {
            switch (ghubMessage.Path)
            {
                case "/devices/list":
                    LoadDevices(ghubMessage.Payload);
                    break;
                case "/battery/state/changed":
                case { } path when BatteryDeviceStateRegex().IsMatch(path):
                    ParseBatteryUpdate(ghubMessage.Payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle G Hub message path={ghubMessage.Path}: {ex}");
        }
    }

    private void LoadDevices(JObject? payload)
    {
        JToken? deviceInfos = payload?["deviceInfos"];
        if (deviceInfos is not JArray devices)
        {
            Debug.WriteLine("G Hub device list did not contain a deviceInfos array.");
            return;
        }

        HashSet<string> currentDeviceIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (JToken deviceToken in devices)
        {
            try
            {
                string? deviceId = deviceToken["id"]?.ToString();
                string? displayName = deviceToken["extendedDisplayName"]?.ToString();
                bool? hasBattery = deviceToken["capabilities"]?["hasBatteryStatus"]?.ToObject<bool>();
                if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(displayName) || !hasBattery.HasValue)
                {
                    Debug.WriteLine("Skipping malformed G Hub device entry.");
                    continue;
                }

                if (!Enum.TryParse(deviceToken["deviceType"]?.ToString(), true, out DeviceType deviceType))
                {
                    deviceType = DeviceType.Mouse;
                }

                currentDeviceIds.Add(deviceId);
                _deviceEventBus.Publish(new InitMessage(deviceId, displayName, hasBattery.Value, deviceType));
                SendRequest("GET", $"/battery/{deviceId}/state");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse G Hub device entry: {ex.Message}");
            }
        }

        string[] missingDeviceIds;
        lock (_deviceSync)
        {
            missingDeviceIds = _knownDeviceIds.Except(currentDeviceIds, StringComparer.OrdinalIgnoreCase).ToArray();
            _knownDeviceIds.Clear();
            _knownDeviceIds.UnionWith(currentDeviceIds);
        }

        foreach (string missingDeviceId in missingDeviceIds)
        {
            _deviceEventBus.Publish(new DeviceOfflineMessage(missingDeviceId));
        }
    }

    private void SignalKnownDevicesOffline(string reason)
    {
        string[] deviceIds;
        lock (_deviceSync)
        {
            deviceIds = _knownDeviceIds.ToArray();
            _knownDeviceIds.Clear();
        }

        foreach (string deviceId in deviceIds)
        {
            _deviceEventBus.Publish(new DeviceOfflineMessage(deviceId));
        }

        if (deviceIds.Length > 0)
        {
            Debug.WriteLine($"Marked {deviceIds.Length} G Hub devices offline: {reason}");
        }
    }

    private void ParseBatteryUpdate(JObject? payload)
    {
        try
        {
            string? deviceId = payload?["deviceId"]?.ToString();
            double? percentage = payload?["percentage"]?.ToObject<double>();
            bool? charging = payload?["charging"]?.ToObject<bool>();
            double mileage = payload?["mileage"]?.ToObject<double>() ?? -1;
            if (string.IsNullOrWhiteSpace(deviceId) || !percentage.HasValue || !double.IsFinite(percentage.Value) ||
                percentage.Value < 0 || percentage.Value > 100 || !charging.HasValue)
            {
                Debug.WriteLine("Rejected malformed G Hub battery payload.");
                return;
            }

            _deviceEventBus.Publish(new UpdateMessage(
                deviceId,
                Math.Clamp(percentage.Value, 0, 100),
                charging.Value
                    ? PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING
                    : PowerSupplyStatus.POWER_SUPPLY_STATUS_NOT_CHARGING,
                0,
                DateTimeOffset.Now,
                double.IsFinite(mileage) ? mileage : -1
            ));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to parse G Hub battery update: {ex.Message}");
        }
    }

    private static async Task<bool> IsTrustedEndpointOwnerAsync(CancellationToken cancellationToken)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            return false;
        }

        string netstatPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "netstat.exe");
        if (!File.Exists(netstatPath))
        {
            return false;
        }

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = netstatPath,
            Arguments = "-ano -p tcp",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            if (!process.Start())
            {
                return false;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) ||
                    !IsLoopbackPort(parts[1], GHubPort) ||
                    !IsZeroRemoteEndpoint(parts[2]) ||
                    !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                {
                    continue;
                }

                return IsTrustedGHubProcess(pid);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Debug.WriteLine($"Failed to validate G Hub port owner: {ex.Message}");
        }

        return false;
    }

    private static bool IsLoopbackPort(string endpoint, int port)
    {
        return endpoint.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) ||
               endpoint.Equals($"[::1]:{port}", StringComparison.OrdinalIgnoreCase) ||
               endpoint.Equals($"0.0.0.0:{port}", StringComparison.OrdinalIgnoreCase) ||
               endpoint.Equals($"[::]:{port}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZeroRemoteEndpoint(string endpoint)
    {
        return endpoint.Equals("0.0.0.0:0", StringComparison.OrdinalIgnoreCase) ||
               endpoint.Equals("[::]:0", StringComparison.OrdinalIgnoreCase) ||
               endpoint.EndsWith(":0", StringComparison.OrdinalIgnoreCase);
    }

    private void TrackBackgroundTask(Task task, string context)
    {
        lock (_taskSync)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(completed =>
        {
            lock (_taskSync)
            {
                _backgroundTasks.Remove(completed);
            }

            if (completed.IsFaulted && completed.Exception != null)
            {
                Debug.WriteLine($"{context} failed: {completed.Exception.GetBaseException()}");
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static bool IsTrustedGHubProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals("lghub_agent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(executablePath);
            string[] trustedRoots =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LGHUB"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LGHUB"),
            ];
            return trustedRoots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

}
