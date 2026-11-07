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
    private readonly object _subscriptionSync = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
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

    public async Task<NativeDiagnosticsResponseMessage?> RequestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        // G Hub-only mode never creates a native IPC session. Export the UI
        // diagnostics without attempting an authenticated native request.
        if (!IpcSessionContext.IsConfigured)
        {
            return null;
        }
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCts.Token, deadline.Token);
        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            await EnsureSubscribedAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            TaskCompletionSource<NativeDiagnosticsResponseMessage> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(requestId, completion))
            {
                return null;
            }

            NativeDiagnosticsRequestMessage request = new(requestId);
            IpcSessionContext.Sign(IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST, request);
            await _publisher.PublishAsync(
                IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST,
                request,
                operation.Token
            );
            return await completion.Task.WaitAsync(operation.Token);
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_lifetimeCts.IsCancellationRequested)
        {
            return null;
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
        IAsyncDisposable? subscription;
        lock (_subscriptionSync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            subscription = _subscription;
            _subscription = null;
        }

        _lifetimeCts.Cancel();
        foreach (TaskCompletionSource<NativeDiagnosticsResponseMessage> completion in _pending.Values)
        {
            completion.TrySetCanceled();
        }
        _pending.Clear();
        subscription?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _lifetimeCts.Dispose();
        // In-flight subscription setup still owns/releases this semaphore after
        // cancellation. It has no wait handle and can be reclaimed with the client.
    }

    private async Task EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        lock (_subscriptionSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscription != null)
            {
                return;
            }
        }

        await _subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            lock (_subscriptionSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_subscription != null)
                {
                    return;
                }
            }

            IAsyncDisposable subscription = await _subscriber.SubscribeAsync(
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
                cancellationToken
            );
            lock (_subscriptionSync)
            {
                if (!_disposed)
                {
                    _subscription = subscription;
                    return;
                }
            }
            await subscription.DisposeAsync();
            throw new ObjectDisposedException(nameof(NativeDiagnosticsClient));
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }
}
