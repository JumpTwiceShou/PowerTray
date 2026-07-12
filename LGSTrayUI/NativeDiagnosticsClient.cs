using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LGSTrayUI;

public sealed class NativeDiagnosticsClient : IDisposable
{
    private readonly IDistributedSubscriber<IPCMessageType, IPCMessage> _subscriber;
    private readonly IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> _publisher;
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NativeDiagnosticsResponseMessage>> _pending = new();

    private IAsyncDisposable? _subscription;
    private bool _disposed;

    public NativeDiagnosticsClient(
        IDistributedSubscriber<IPCMessageType, IPCMessage> subscriber,
        IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> publisher
    )
    {
        _subscriber = subscriber;
        _publisher = publisher;
    }

    public async Task<NativeDiagnosticsResponseMessage?> RequestAsync(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureSubscribedAsync();

        string requestId = Guid.NewGuid().ToString("N");
        TaskCompletionSource<NativeDiagnosticsResponseMessage> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            return null;
        }

        try
        {
            NativeDiagnosticsRequestMessage request = new(requestId);
            IpcSessionContext.Sign(IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST, request);
            await _publisher.PublishAsync(
                IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST,
                request
            );
            return await completion.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TaskCompletionSource<NativeDiagnosticsResponseMessage> completion in _pending.Values)
        {
            completion.TrySetCanceled();
        }
        _pending.Clear();
        _subscription?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _subscription = null;
        _subscriptionLock.Dispose();
    }

    private async Task EnsureSubscribedAsync()
    {
        if (_subscription != null)
        {
            return;
        }

        await _subscriptionLock.WaitAsync();
        try
        {
            if (_subscription != null)
            {
                return;
            }

            _subscription = await _subscriber.SubscribeAsync(
                IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE,
                message =>
                {
                    if (message is not NativeDiagnosticsResponseMessage response ||
                        !IpcSessionContext.Validate(IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE, response))
                    {
                        return;
                    }

                    if (_pending.TryGetValue(response.requestId, out TaskCompletionSource<NativeDiagnosticsResponseMessage>? completion))
                    {
                        completion.TrySetResult(response);
                    }
                },
                CancellationToken.None
            );
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }
}
