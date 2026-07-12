using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LGSTrayCore.Managers;

public sealed class LGSTrayHIDManager : IDeviceManager, IHostedService, IDisposable, IAsyncDisposable
{
    private const int FastFailureWindowSeconds = 20;
    private const int MaxRestartDelaySeconds = 30;

    private readonly CancellationTokenSource _cts = new();
    private readonly object _daemonSync = new();
    private readonly IDistributedSubscriber<IPCMessageType, IPCMessage> _subscriber;
    private readonly IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> _requestPublisher;
    private readonly IPublisher<IPCMessage> _deviceEventBus;
    private readonly NativeBackendStatus _status;
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NativeRediscoverResponseMessage>> _pendingRediscover = new();

    private CancellationTokenSource? _daemonCts;
    private Task? _supervisorTask;
    private string? _ipcToken;
    private bool _disposed;

    public LGSTrayHIDManager(
        IDistributedSubscriber<IPCMessageType, IPCMessage> subscriber,
        IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> requestPublisher,
        IPublisher<IPCMessage> deviceEventBus,
        NativeBackendStatus status
    )
    {
        _subscriber = subscriber;
        _requestPublisher = requestPublisher;
        _deviceEventBus = deviceEventBus;
        _status = status;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ipcToken ??= IpcSessionContext.CreateAndSetToken();

        _subscriptions.Add(await SubscribeValidatedAsync(IPCMessageType.INIT, message =>
            _deviceEventBus.Publish((InitMessage)message), cancellationToken));
        _subscriptions.Add(await SubscribeValidatedAsync(IPCMessageType.UPDATE, message =>
            _deviceEventBus.Publish((UpdateMessage)message), cancellationToken));
        _subscriptions.Add(await SubscribeValidatedAsync(IPCMessageType.OFFLINE, message =>
            _deviceEventBus.Publish((DeviceOfflineMessage)message), cancellationToken));
        _subscriptions.Add(await SubscribeValidatedAsync(IPCMessageType.HEARTBEAT, message =>
            _status.MarkHeartbeat((HeartbeatMessage)message), cancellationToken));
        _subscriptions.Add(await SubscribeValidatedAsync(IPCMessageType.NATIVE_REDISCOVER_RESPONSE, message =>
        {
            NativeRediscoverResponseMessage response = (NativeRediscoverResponseMessage)message;
            if (_pendingRediscover.TryGetValue(response.requestId, out TaskCompletionSource<NativeRediscoverResponseMessage>? completion))
            {
                completion.TrySetResult(response);
            }
        }, cancellationToken));

        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        CancelDaemon();

        if (_supervisorTask != null)
        {
            try
            {
                await _supervisorTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _supervisorTask = null;
            }
        }

        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }
        _subscriptions.Clear();
        foreach (TaskCompletionSource<NativeRediscoverResponseMessage> completion in _pendingRediscover.Values)
        {
            completion.TrySetCanceled();
        }
        _pendingRediscover.Clear();
        _status.MarkStopped();
    }

    public async Task RediscoverDevicesAsync(CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        TaskCompletionSource<NativeRediscoverResponseMessage> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRediscover.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Could not allocate a rediscover request.");
        }

        try
        {
            NativeRediscoverRequestMessage request = new(requestId);
            IpcSessionContext.Sign(IPCMessageRequestType.NATIVE_REDISCOVER_REQUEST, request);
            await _requestPublisher.PublishAsync(
                IPCMessageRequestType.NATIVE_REDISCOVER_REQUEST,
                request,
                cancellationToken
            );

            NativeRediscoverResponseMessage response = await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(15),
                cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(response.error))
            {
                throw new InvalidOperationException($"PowerTrayHID rediscover failed: {response.error}");
            }
        }
        catch (TimeoutException)
        {
            CancelDaemon();
            throw;
        }
        finally
        {
            _pendingRediscover.TryRemove(requestId, out _);
        }
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeBackendStatusSnapshot snapshot = _status.Snapshot;
        bool heartbeatExpired = snapshot.LastHeartbeatAt.HasValue &&
                                DateTimeOffset.UtcNow - snapshot.LastHeartbeatAt.Value > TimeSpan.FromSeconds(35);
        bool startupHeartbeatMissing = snapshot.State.Equals("starting", StringComparison.OrdinalIgnoreCase) &&
                                       !snapshot.LastHeartbeatAt.HasValue;
        if (heartbeatExpired || startupHeartbeatMissing)
        {
            Debug.WriteLine("PowerTrayHID heartbeat is missing or expired; restarting helper.");
            CancelDaemon();
            return;
        }

        try
        {
            NativeHealthCheckRequestMessage request = new(Guid.NewGuid().ToString("N"));
            IpcSessionContext.Sign(IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST, request);
            await _requestPublisher.PublishAsync(
                IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST,
                request,
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"PowerTrayHID health request failed: {ex.Message}");
            CancelDaemon();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        CancelDaemon();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await StopAsync(timeout.Token);
        }
        finally
        {
            Dispose();
        }
    }

    private async Task<IAsyncDisposable> SubscribeValidatedAsync(
        IPCMessageType type,
        Action<IPCMessage> handler,
        CancellationToken cancellationToken
    )
    {
        return await _subscriber.SubscribeAsync(type, message =>
        {
            try
            {
                if (!IpcSessionContext.Validate(type, message))
                {
                    Debug.WriteLine($"Rejected unauthenticated PowerTray IPC message: {type}");
                    return;
                }

                handler(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PowerTray IPC handler failed for {type}: {ex}");
            }
        }, cancellationToken);
    }

    private async Task SupervisorLoopAsync(CancellationToken cancellationToken)
    {
        int restartCount = 0;
        int fastFailCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            _status.MarkStarting(restartCount);
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            DaemonResult result = await RunDaemonOnceAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            bool fastFailure = DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(FastFailureWindowSeconds);
            fastFailCount = fastFailure ? fastFailCount + 1 : 0;
            restartCount++;
            _status.MarkFault(result.Error ?? $"PowerTrayHID exited with code {result.ExitCode}.", restartCount);

            int delaySeconds = Math.Min(MaxRestartDelaySeconds, 1 << Math.Min(fastFailCount, 5));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _status.MarkStopped();
    }

    private async Task<DaemonResult> RunDaemonOnceAsync(CancellationToken supervisorToken)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "PowerTrayHID.exe"),
            Arguments = Environment.ProcessId.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        process.StartInfo.Environment[IpcSessionContext.EnvironmentVariableName] =
            _ipcToken ?? throw new InvalidOperationException("PowerTray IPC session is not initialized.");

        CancellationTokenSource daemonCts = new();
        lock (_daemonSync)
        {
            _daemonCts?.Dispose();
            _daemonCts = daemonCts;
        }

        try
        {
            if (!process.Start())
            {
                return new DaemonResult(-1, "PowerTrayHID process did not start.");
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(supervisorToken, daemonCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                await TerminateProcessAsync(process);
            }

            if (!process.HasExited)
            {
                await TerminateProcessAsync(process);
            }

            return new DaemonResult(process.HasExited ? process.ExitCode : -1, null);
        }
        catch (Exception ex)
        {
            await TerminateProcessAsync(process);
            return new DaemonResult(-1, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            lock (_daemonSync)
            {
                if (ReferenceEquals(_daemonCts, daemonCts))
                {
                    _daemonCts = null;
                }
            }
            daemonCts.Dispose();
        }
    }

    private void CancelDaemon()
    {
        lock (_daemonSync)
        {
            try
            {
                _daemonCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task TerminateProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            if (!process.HasExited)
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Debug.WriteLine($"Failed to terminate PowerTrayHID cleanly: {ex.Message}");
        }
    }

    private sealed record DaemonResult(int ExitCode, string? Error);
}
