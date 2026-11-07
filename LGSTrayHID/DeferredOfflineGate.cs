using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID;

internal sealed class DeferredOfflineGate : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CancellationTokenSource> _deferredOfflineSignals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _pendingTasks = [];
    private readonly Action<string>? _addDiagnosticEvent;
    private readonly Func<string, string> _hashDeviceIdForDiagnostics;

    private DateTimeOffset _deferOfflineUntil = DateTimeOffset.MinValue;
    private bool _disposed;

    public DeferredOfflineGate(
        Action<string>? addDiagnosticEvent = null,
        Func<string, string>? hashDeviceIdForDiagnostics = null
    )
    {
        _addDiagnosticEvent = addDiagnosticEvent;
        _hashDeviceIdForDiagnostics = hashDeviceIdForDiagnostics ?? (static x => x);
    }

    public void BeginDeferral(string reason, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        DateTimeOffset until = DateTimeOffset.Now.Add(duration);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (until > _deferOfflineUntil)
            {
                _deferOfflineUntil = until;
            }
        }

        _addDiagnosticEvent?.Invoke($"Deferring offline signals for {(int)duration.TotalMilliseconds}ms after {reason}");
    }

    public bool TryDefer(DeviceOfflineMessage offlineMessage, Action<DeviceOfflineMessage> emitOffline)
    {
        if (string.IsNullOrWhiteSpace(offlineMessage.deviceId))
        {
            return false;
        }

        TimeSpan delay;
        CancellationTokenSource cts = new();
        lock (_sync)
        {
            if (_disposed)
            {
                cts.Dispose();
                return false;
            }

            delay = _deferOfflineUntil - DateTimeOffset.Now;
            if (delay <= TimeSpan.Zero)
            {
                cts.Dispose();
                return false;
            }

            if (_deferredOfflineSignals.Remove(offlineMessage.deviceId, out CancellationTokenSource? previousCts))
            {
                previousCts.Cancel();
            }

            _deferredOfflineSignals[offlineMessage.deviceId] = cts;

            // Register before cancellation/shutdown can snapshot the pending work.
            Task pendingTask = Task.Run(() => EmitAfterDelayAsync(offlineMessage, emitOffline, delay, cts));
            _pendingTasks.Add(pendingTask);
            _ = pendingTask.ContinueWith(completed =>
            {
                lock (_sync)
                {
                    _pendingTasks.Remove(completed);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        _addDiagnosticEvent?.Invoke(
            $"Deferred offline for device hash={_hashDeviceIdForDiagnostics(offlineMessage.deviceId)}"
        );

        return true;
    }

    private async Task EmitAfterDelayAsync(
        DeviceOfflineMessage offlineMessage,
        Action<DeviceOfflineMessage> emitOffline,
        TimeSpan delay,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token);
            lock (_sync)
            {
                if (!_deferredOfflineSignals.TryGetValue(offlineMessage.deviceId, out CancellationTokenSource? currentCts)
                    || !ReferenceEquals(currentCts, cts))
                {
                    return;
                }

                _deferredOfflineSignals.Remove(offlineMessage.deviceId);
            }

            emitOffline(offlineMessage);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _addDiagnosticEvent?.Invoke($"Deferred offline callback failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Cancel and Dispose must share ownership even after removal
            // from the dictionary (CancelAll can still hold this source).
            lock (_sync)
            {
                cts.Dispose();
            }
        }
    }

    public bool Cancel(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_deferredOfflineSignals.Remove(deviceId, out CancellationTokenSource? cts))
            {
                return false;
            }
            cts.Cancel();
        }
        _addDiagnosticEvent?.Invoke(
            $"Cancelled deferred offline for device hash={_hashDeviceIdForDiagnostics(deviceId)}"
        );
        return true;
    }

    public void CancelAll()
    {
        lock (_sync)
        {
            CancellationTokenSource[] deferredSignals = [.. _deferredOfflineSignals.Values];
            _deferredOfflineSignals.Clear();
            _deferOfflineUntil = DateTimeOffset.MinValue;
            foreach (CancellationTokenSource cts in deferredSignals)
            {
                cts.Cancel();
            }
        }
    }

    public async Task WaitForPendingAsync(CancellationToken cancellationToken)
    {
        Task[] tasks;
        lock (_sync)
        {
            tasks = _pendingTasks.ToArray();
        }

        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        CancelAll();
    }
}
