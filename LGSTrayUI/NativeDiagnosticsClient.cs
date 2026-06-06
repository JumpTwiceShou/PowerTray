using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LGSTrayUI;

public sealed class NativeDiagnosticsClient : IDisposable
{
    private readonly IDistributedSubscriber<IPCMessageType, IPCMessage> _subscriber;
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly object _sync = new();
    private IAsyncDisposable? _subscription;
    private NativeDiagnosticsResponseMessage? _latest;
    private TaskCompletionSource<NativeDiagnosticsResponseMessage>? _nextSnapshot;
    private bool _disposed;

    public NativeDiagnosticsClient(IDistributedSubscriber<IPCMessageType, IPCMessage> subscriber)
    {
        _subscriber = subscriber;
    }

    public async Task<NativeDiagnosticsResponseMessage?> RequestAsync(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureSubscribedAsync();

        Task<NativeDiagnosticsResponseMessage> waitTask;
        lock (_sync)
        {
            if (_latest != null)
            {
                return _latest;
            }

            _nextSnapshot ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _nextSnapshot.Task;
        }

        try
        {
            return await waitTask.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            lock (_sync)
            {
                return _latest;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
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
                    if (message is not NativeDiagnosticsResponseMessage response)
                    {
                        return;
                    }

                    lock (_sync)
                    {
                        _latest = response;
                        _nextSnapshot?.TrySetResult(response);
                        _nextSnapshot = null;
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
