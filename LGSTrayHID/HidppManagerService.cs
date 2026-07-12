using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Hosting;

namespace LGSTrayHID;

public sealed class HidppManagerService : IHostedService, IAsyncDisposable
{
    private readonly IDistributedPublisher<IPCMessageType, IPCMessage> _publisher;
    private readonly IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage> _requestSubscriber;
    private readonly CancellationTokenSource _serviceCts = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly object _taskSync = new();
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly HashSet<Task> _requestTasks = [];

    private Task? _heartbeatTask;
    private int _stopped;

    public HidppManagerService(
        IDistributedPublisher<IPCMessageType, IPCMessage> publisher,
        IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage> requestSubscriber
    )
    {
        _publisher = publisher;
        _requestSubscriber = requestSubscriber;
        HidppManagerContext.Instance.HidppDeviceEvent += OnHidppDeviceEvent;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(await SubscribeRequestAsync(
            IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST,
            request => HandleDiagnosticsRequestAsync(request),
            "diagnostics request",
            cancellationToken
        ));
        _subscriptions.Add(await SubscribeRequestAsync(
            IPCMessageRequestType.BATTERY_UPDATE_REQUEST,
            request => HandleBatteryUpdateRequestAsync(request),
            "battery update request",
            cancellationToken
        ));
        _subscriptions.Add(await SubscribeRequestAsync(
            IPCMessageRequestType.NATIVE_REDISCOVER_REQUEST,
            request => HandleRediscoverRequestAsync(request),
            "rediscover request",
            cancellationToken
        ));
        _subscriptions.Add(await SubscribeRequestAsync(
            IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST,
            request => HandleHealthCheckRequestAsync(request),
            "health check request",
            cancellationToken
        ));

        _heartbeatTask = Task.Run(() => RunHeartbeatLoopAsync(_serviceCts.Token), CancellationToken.None);
        HidppManagerContext.Instance.Start(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _serviceCts.Cancel();
        HidppManagerContext.Instance.HidppDeviceEvent -= OnHidppDeviceEvent;

        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }
        _subscriptions.Clear();

        await WaitTaskAsync(_heartbeatTask, cancellationToken);
        _heartbeatTask = null;

        Task[] requestTasks;
        lock (_taskSync)
        {
            requestTasks = _requestTasks.ToArray();
        }
        if (requestTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(requestTasks).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                NativeDiagnosticsStore.RecordError($"IPC request shutdown failed: {ex.GetBaseException().Message}");
            }
        }

        await HidppManagerContext.Instance.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await StopAsync(timeout.Token);
        _serviceCts.Dispose();
    }

    private async Task<IAsyncDisposable> SubscribeRequestAsync(
        IPCMessageRequestType type,
        Func<IPCRequestMessage, Task> handler,
        string context,
        CancellationToken cancellationToken
    )
    {
        return await _requestSubscriber.SubscribeAsync(
            type,
            request => TrackRequestTask(handler(request), context),
            cancellationToken
        );
    }

    private void OnHidppDeviceEvent(IPCMessageType type, IPCMessage message)
    {
        TrackRequestTask(PublishAuthenticatedAsync(type, message), $"publish {type}");
    }

    private void TrackRequestTask(Task task, string context)
    {
        lock (_taskSync)
        {
            _requestTasks.Add(task);
        }

        _ = task.ContinueWith(completed =>
        {
            lock (_taskSync)
            {
                _requestTasks.Remove(completed);
            }

            if (completed.IsFaulted && completed.Exception != null)
            {
                NativeDiagnosticsStore.RecordError($"{context} failed: {completed.Exception.GetBaseException().Message}");
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PublishAuthenticatedAsync(
                    IPCMessageType.HEARTBEAT,
                    new HeartbeatMessage(
                        Environment.ProcessId,
                        _startedAt,
                        NativeDiagnosticsStore.LastSuccessfulCommandAt,
                        "running",
                        NativeDiagnosticsStore.LastError
                    ),
                    cancellationToken
                );

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                NativeDiagnosticsStore.RecordError($"Heartbeat publish failed: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleDiagnosticsRequestAsync(IPCRequestMessage request)
    {
        if (!IpcSessionContext.Validate(IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST, request) ||
            request is not NativeDiagnosticsRequestMessage diagnosticsRequest ||
            string.IsNullOrWhiteSpace(diagnosticsRequest.requestId))
        {
            return;
        }

        await PublishDiagnosticsSnapshotAsync(diagnosticsRequest.requestId);
    }

    private async Task HandleBatteryUpdateRequestAsync(IPCRequestMessage request)
    {
        if (!IpcSessionContext.Validate(IPCMessageRequestType.BATTERY_UPDATE_REQUEST, request) ||
            request is not BatteryUpdateRequestMessage)
        {
            return;
        }

        await HidppManagerContext.Instance.ForceBatteryUpdates();
    }

    private async Task HandleRediscoverRequestAsync(IPCRequestMessage request)
    {
        if (!IpcSessionContext.Validate(IPCMessageRequestType.NATIVE_REDISCOVER_REQUEST, request) ||
            request is not NativeRediscoverRequestMessage rediscoverRequest ||
            string.IsNullOrWhiteSpace(rediscoverRequest.requestId))
        {
            return;
        }

        string? error = null;
        try
        {
            await HidppManagerContext.Instance.RediscoverDevicesAsync("manualRequest");
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            NativeDiagnosticsStore.RecordError($"Manual rediscover failed: {error}");
        }

        await PublishAuthenticatedAsync(
            IPCMessageType.NATIVE_REDISCOVER_RESPONSE,
            new NativeRediscoverResponseMessage(rediscoverRequest.requestId, error)
        );
    }

    private async Task HandleHealthCheckRequestAsync(IPCRequestMessage request)
    {
        if (!IpcSessionContext.Validate(IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST, request) ||
            request is not NativeHealthCheckRequestMessage)
        {
            return;
        }

        await HidppManagerContext.Instance.ProbePresenceAsync(_serviceCts.Token);
    }

    private Task PublishDiagnosticsSnapshotAsync(string requestId)
    {
        return PublishAuthenticatedAsync(
            IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE,
            new NativeDiagnosticsResponseMessage(
                requestId,
                NativeDiagnosticsStore.GetJson(),
                NativeDiagnosticsStore.GetSummary()
            )
        );
    }

    private async Task PublishAuthenticatedAsync(
        IPCMessageType type,
        IPCMessage message,
        CancellationToken cancellationToken = default
    )
    {
        IpcSessionContext.Sign(type, message);
        await _publisher.PublishAsync(type, message, cancellationToken);
    }

    private static async Task WaitTaskAsync(Task? task, CancellationToken cancellationToken)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
