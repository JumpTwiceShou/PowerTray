using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID;

// One sender and a bounded event queue. Preserve presence transitions because
// OFFLINE resets the UI's alert cycle; coalesce only equivalent battery states
// between transitions. Threshold crossings and charging changes also reset alerts.
internal sealed class DeviceEventOutbox
{
    private readonly object _sync = new();
    private readonly Queue<PendingEvent> _pending = new();
    private readonly Dictionary<string, PendingEvent> _updates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly SemaphoreSlim _available = new(0);
    private readonly SemaphoreSlim _slots;
    private readonly CancellationToken _stop;
    private readonly Func<IPCMessageType, IPCMessage, CancellationToken, Task> _publish;

    internal Task Completion { get; }

    internal DeviceEventOutbox(
        Func<IPCMessageType, IPCMessage, CancellationToken, Task> publish,
        CancellationToken stop,
        int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _publish = publish;
        _stop = stop;
        _slots = new(capacity, capacity);
        Completion = Task.Run(RunAsync);
    }

    internal void Enqueue(IPCMessageType type, IPCMessage message)
    {
        if (type is not (IPCMessageType.INIT or IPCMessageType.UPDATE or IPCMessageType.OFFLINE))
            throw new ArgumentOutOfRangeException(nameof(type));

        // Keep admission serialized even while full: a later UPDATE must not
        // bypass an OFFLINE waiting for a slot and merge into the previous cycle.
        _admission.Wait(_stop);
        try
        {
            lock (_sync)
            {
                _stop.ThrowIfCancellationRequested();
                if (type == IPCMessageType.UPDATE && _updates.TryGetValue(message.deviceId, out PendingEvent? update) &&
                    update.Message is UpdateMessage previous && message is UpdateMessage next &&
                    previous.batteryPercentage == next.batteryPercentage && previous.powerSupplyStatus == next.powerSupplyStatus)
                {
                    // The UI owns configurable thresholds. Preserve every change
                    // that could reset its alert cycle; only refresh equivalent samples.
                    update.Message = message;
                    return;
                }
            }

            _slots.Wait(_stop);
            bool added = false;
            try
            {
                lock (_sync)
                {
                    _stop.ThrowIfCancellationRequested();
                    PendingEvent pending = new(type, message);
                    _pending.Enqueue(pending);
                    if (type == IPCMessageType.UPDATE) _updates[message.deviceId] = pending;
                    else _updates.Remove(message.deviceId);
                    added = true;
                }
            }
            finally
            {
                if (!added) _slots.Release();
            }
            _available.Release();
        }
        finally
        {
            _admission.Release();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(_stop).ConfigureAwait(false);
                PendingEvent pending;
                lock (_sync)
                {
                    pending = _pending.Dequeue();
                    if (_updates.TryGetValue(pending.Message.deviceId, out PendingEvent? update) && ReferenceEquals(update, pending))
                        _updates.Remove(pending.Message.deviceId);
                }
                _slots.Release();
                await PublishAsync(pending.Type, pending.Message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            lock (_sync)
            {
                _pending.Clear();
                _updates.Clear();
            }
            // Enqueue callbacks may still be unwinding cancellation. No wait
            // handles were requested; the semaphores are reclaimed with the outbox.
        }
    }

    private async Task PublishAsync(IPCMessageType type, IPCMessage message)
    {
        try
        {
            _stop.ThrowIfCancellationRequested();
            await _publish(type, message, _stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            NativeDiagnosticsStore.RecordError($"Publish {type} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class PendingEvent(IPCMessageType type, IPCMessage message)
    {
        internal readonly IPCMessageType Type = type;
        internal IPCMessage Message = message;
    }
}
