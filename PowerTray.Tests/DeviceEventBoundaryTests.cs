using LGSTrayHID;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using System.Collections.Concurrent;
using System.Reflection;

internal static class DeviceEventBoundaryTests
{
    internal static async Task RunAsync()
    {
        await TestWaitingBoundaryAsync(cancel: false);
        await TestWaitingBoundaryAsync(cancel: true);
        await TestSingleDeviceCapacityAsync();
        Console.WriteLine("Queued presence boundaries, single-device capacity, and both admission-wait cancellations passed.");
    }

    private static async Task TestWaitingBoundaryAsync(bool cancel)
    {
        using CancellationTokenSource stop = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<IPCMessage> sent = new();
        DeviceEventOutbox outbox = new(async (_, message, token) =>
        {
            if (message.deviceId == "blocker")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            sent.Enqueue(message);
            if (message is UpdateMessage { deviceId: "target", batteryMVolt: 2000 }) drained.TrySetResult();
        }, stop.Token, capacity: 2);
        List<Task> producers = [];
        try
        {
            outbox.Enqueue(IPCMessageType.UPDATE, Update("blocker", 0));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            outbox.Enqueue(IPCMessageType.UPDATE, Update("target", 1, 1000));
            outbox.Enqueue(IPCMessageType.INIT, new InitMessage("other", "Other", true, DeviceType.Mouse));
            producers.Add(Task.Factory.StartNew(() => outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("target")),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            // Establish the exact interleaving: OFFLINE owns admission and waits
            // for capacity while the preceding UPDATE is still eligible to merge.
            SemaphoreSlim admission = (SemaphoreSlim)typeof(DeviceEventOutbox)
                .GetField("_admission", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outbox)!;
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            while (admission.CurrentCount != 0) await Task.Delay(1, timeout.Token);
            using ManualResetEventSlim attempting = new();
            producers.Add(Task.Factory.StartNew(() =>
            {
                attempting.Set();
                // Same alert state would normally be eligible to coalesce. The
                // changed voltage lets us distinguish the pre/post-OFFLINE frames.
                outbox.Enqueue(IPCMessageType.UPDATE, Update("target", 1, 2000));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            Check(attempting.Wait(TimeSpan.FromSeconds(2)), "Later update must attempt admission.");
            await Task.Delay(100);
            Check(producers.All(task => !task.IsCompleted), "UPDATE must not bypass a pending OFFLINE or overwrite the preceding cycle's reading.");
            if (cancel)
            {
                stop.Cancel();
                foreach (Task task in producers) await ExpectCanceledAsync(task);
            }
            else
            {
                release.TrySetResult();
                await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(2));
                await drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
                IPCMessage[] target = sent.Where(message => message.deviceId == "target").ToArray();
                Check(target.Length == 3 && target[0] is UpdateMessage { batteryPercentage: 1, batteryMVolt: 1000 } &&
                      target[1] is DeviceOfflineMessage && target[2] is UpdateMessage { batteryPercentage: 1, batteryMVolt: 2000 },
                    "Backpressure must preserve UPDATE(old), OFFLINE, UPDATE(new) in that order.");
            }
        }
        finally
        {
            stop.Cancel();
            foreach (Task task in producers)
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) { }
            }
            await outbox.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task TestSingleDeviceCapacityAsync()
    {
        using CancellationTokenSource stop = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DeviceEventOutbox outbox = new(async (_, _, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, stop.Token, capacity: 2);
        Task? producer = null;
        try
        {
            outbox.Enqueue(IPCMessageType.UPDATE, Update("blocker", 0));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            outbox.Enqueue(IPCMessageType.INIT, new InitMessage("target", "Target", true, DeviceType.Mouse));
            outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("target"));
            using ManualResetEventSlim attempting = new();
            producer = Task.Factory.StartNew(() =>
            {
                attempting.Set();
                outbox.Enqueue(IPCMessageType.INIT, new InitMessage("target", "Target", true, DeviceType.Mouse));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Check(attempting.Wait(TimeSpan.FromSeconds(2)), "Reconnect must attempt admission.");
            await Task.Delay(100);
            Check(!producer.IsCompleted, "Repeated transitions for one device must count against the bounded event capacity.");
        }
        finally
        {
            stop.Cancel();
            if (producer != null) await ExpectCanceledAsync(producer);
            await outbox.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static UpdateMessage Update(string id, int battery, int millivolts = 0) =>
        new(id, battery, PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, millivolts, DateTimeOffset.UtcNow);

    private static async Task ExpectCanceledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Expected cancellation of admission wait.");
        }
        catch (OperationCanceledException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
