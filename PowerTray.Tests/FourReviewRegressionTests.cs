using LGSTrayHID;
using LGSTrayPrimitives;
using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayUI;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

internal static class FourReviewRegressionTests
{
    internal static async Task RunAsync()
    {
        await TestFreshProcessDiagnosticsAsync();
        await TestOfflineCancellationOwnershipAsync("single");
        await TestOfflineCancellationOwnershipAsync("all");
        await TestOfflineCancellationOwnershipAsync("dispose");
        await TestOfflineReplacementAsync();
        await TestOutboxCoalescingAsync();
        await TestOutboxCapacityAsync();
        await TestServiceShutdownAsync();
        await TestServiceCapacityShutdownAsync();
        await PipeFailureTests.RunAsync();
        Console.WriteLine("All four review regression suites passed.");
    }

    internal static async Task TestUnconfiguredDiagnosticsAsync()
    {
        Check(!IpcSessionContext.IsConfigured, "This regression must run in a fresh process without a native IPC session.");
        await using ServiceProvider provider = CreateProvider();
        using NativeDiagnosticsClient client = new(
            provider.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>(),
            new UnexpectedPublisher());
        Check(await client.RequestAsync(TimeSpan.FromSeconds(2)) == null,
            "G Hub-only diagnostics must return unavailable instead of trying to sign a request.");
        Check(Field(client, "_subscription") == null, "Unavailable native diagnostics must not create a subscription.");
        using CancellationTokenSource caller = new();
        caller.Cancel();
        await ExpectCanceledAsync(client.RequestAsync(TimeSpan.FromSeconds(2), caller.Token));
        client.Dispose();
        try
        {
            await client.RequestAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Disposed diagnostics client must still reject requests.");
        }
        catch (ObjectDisposedException) { }
        Console.WriteLine("Fresh-process native-disabled diagnostics passed.");
    }

    private static async Task TestFreshProcessDiagnosticsAsync()
    {
        ProcessStartInfo start = new(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(FourReviewRegressionTests).Assembly.Location);
        start.ArgumentList.Add("--diagnostics-unconfigured-child");
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Check(process.ExitCode == 0, "Fresh-process regression failed: " + await stderr);
        Console.Write(await stdout);
    }

    private static async Task TestOfflineCancellationOwnershipAsync(string mode)
    {
        using DeferredOfflineGate gate = new();
        gate.BeginDeferral("cancellation-ownership", TimeSpan.FromMilliseconds(300));
        int emitted = 0;
        Check(gate.TryDefer(new("first"), _ => Interlocked.Increment(ref emitted)), "First offline must defer.");
        Check(gate.TryDefer(new("second"), _ => Interlocked.Increment(ref emitted)), "Second offline must defer.");
        var sources = (Dictionary<string, CancellationTokenSource>)Field(gate, "_deferredOfflineSignals")!;
        CancellationTokenSource first = sources["first"];
        CancellationTokenSource observed = mode == "single" ? first : sources["second"];
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        // Hold cancellation after the source was removed. The timer is allowed to
        // expire, but it must not dispose a CTS still owned by Cancel/CancelAll.
        using CancellationTokenRegistration registration = first.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        Task cancel = Task.Run(() =>
        {
            if (mode == "single") gate.Cancel("first");
            else if (mode == "all") gate.CancelAll();
            else gate.Dispose();
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(600);
            _ = observed.Token; // Previously threw ObjectDisposedException here.
        }
        finally { release.Set(); }
        await cancel.WaitAsync(TimeSpan.FromSeconds(2));
        await gate.WaitForPendingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Check(emitted == (mode == "single" ? 1 : 0), "Canceled offline signals must not be emitted.");
    }

    private static async Task TestOfflineReplacementAsync()
    {
        using DeferredOfflineGate gate = new();
        gate.BeginDeferral("replace", TimeSpan.FromMilliseconds(300));
        int emitted = 0;
        for (int i = 0; i < 200; i++)
            Check(gate.TryDefer(new("device"), _ => Interlocked.Increment(ref emitted)), "Replacement must defer.");
        await gate.WaitForPendingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        Check(emitted == 1, "Replacing a deferred offline must emit only the latest signal.");
    }

    private static async Task TestOutboxCoalescingAsync()
    {
        using CancellationTokenSource stop = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<(IPCMessageType Type, IPCMessage Message)> sent = new();
        DeviceEventOutbox outbox = new(async (type, message, token) =>
        {
            if (message.deviceId == "blocker")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            sent.Enqueue((type, message));
            if (message.deviceId == "sentinel") drained.TrySetResult();
        }, stop.Token, capacity: 16);
        try
        {
            outbox.Enqueue(IPCMessageType.UPDATE, Update("blocker", 0));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            foreach (string id in new[] { "offline", "online", "recreated" })
                outbox.Enqueue(IPCMessageType.INIT, new InitMessage(id, "original", true, DeviceType.Mouse));
            for (int i = 0; i < 5000; i++)
                outbox.Enqueue(IPCMessageType.UPDATE, Update("offline", 99));
            outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("offline"));
            outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("online"));
            outbox.Enqueue(IPCMessageType.UPDATE, Update("online", 75));
            outbox.Enqueue(IPCMessageType.UPDATE, Update("recreated", 80));
            outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("recreated"));
            outbox.Enqueue(IPCMessageType.INIT, new InitMessage("recreated", "replacement", true, DeviceType.Mouse));
            // HID++ online notifications can send INIT without a fresh battery
            // UPDATE. Neither that INIT nor OFFLINE may discard known battery data.
            outbox.Enqueue(IPCMessageType.UPDATE, Update("sentinel", 0));
            Check(sent.IsEmpty, "Only the single sender may call the blocked publisher.");
            release.TrySetResult();
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var messages = sent.ToArray();
            Check(messages.Length == 12, "Updates must coalesce while all presence transitions remain queued.");
            var offline = messages.Where(item => item.Message.deviceId == "offline").ToArray();
            Check(offline[0].Message is InitMessage && offline[1].Message is UpdateMessage { batteryPercentage: 99 } &&
                  offline[2].Message is DeviceOfflineMessage, "Offline devices must retain their last battery update after INIT.");
            var online = messages.Where(item => item.Message.deviceId == "online").ToArray();
            Check(online[0].Message is InitMessage && online[1].Message is DeviceOfflineMessage &&
                  online[2].Message is UpdateMessage { batteryPercentage: 75 }, "The offline cycle boundary must precede the new online update.");
            var recreated = messages.Where(item => item.Message.deviceId == "recreated").ToArray();
            Check(recreated[0].Message is InitMessage && recreated[1].Message is UpdateMessage { batteryPercentage: 80 } &&
                  recreated[2].Message is DeviceOfflineMessage && recreated[3].Message is InitMessage { deviceName: "replacement" },
                "Repeated INIT must preserve the last battery and the intermediate offline boundary.");
        }
        finally
        {
            stop.Cancel();
            await outbox.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task TestOutboxCapacityAsync()
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
            outbox.Enqueue(IPCMessageType.UPDATE, Update("in-flight", 1));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            outbox.Enqueue(IPCMessageType.UPDATE, Update("one", 1));
            outbox.Enqueue(IPCMessageType.UPDATE, Update("two", 2));
            // A pending UPDATE can still merge at capacity before another
            // producer owns admission for a queued presence transition.
            outbox.Enqueue(IPCMessageType.UPDATE, Update("one", 1));
            using ManualResetEventSlim attempting = new();
            producer = Task.Factory.StartNew(() =>
            {
                attempting.Set();
                outbox.Enqueue(IPCMessageType.OFFLINE, new DeviceOfflineMessage("three"));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Check(attempting.Wait(TimeSpan.FromSeconds(2)), "Producer must attempt admission.");
            await Task.Delay(100);
            Check(!producer.IsCompleted, "A distinct device beyond capacity must apply backpressure.");
        }
        finally
        {
            stop.Cancel();
            if (producer != null) await ExpectCanceledAsync(producer);
            await outbox.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task TestServiceShutdownAsync()
    {
        _ = IpcSessionContext.CreateAndSetToken();
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageType, IPCMessage> publisher = new("PowerTray.Tests.Service." + Guid.NewGuid().ToString("N"));
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 257; i++)
            await publisher.PublishAsync(IPCMessageType.UPDATE, Update("fill", i), deadline.Token);
        // Do not StartAsync: no real HID helper, enumeration or callbacks are started.
        await using HidppManagerService service = new(publisher,
            provider.GetRequiredService<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>());
        for (int i = 0; i < 5000; i++)
            HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, Update("flood", 50));
        Check(((HashSet<Task>)Field(service, "_requestTasks")!).Count == 0,
            "Device events must not create thousands of tasks outside the transport queue.");
        NativeDiagnosticsRequestMessage request = new("shutdown-response");
        IpcSessionContext.Sign(IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST, request);
        Task response = (Task)typeof(HidppManagerService).GetMethod("HandleDiagnosticsRequestAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [request])!;
        Check(!response.IsCompleted, "Diagnostics response must be waiting on the full transport.");
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await ExpectCanceledAsync(response);
        Check(((DeviceEventOutbox)Field(service, "_deviceEvents")!).Completion.IsCompletedSuccessfully,
            "Stopping the service must drain the event sender's lifetime without an external timeout.");
        HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, Update("after-stop", 10));
        Console.WriteLine("5000-event full-pipe flood and service/reply cancellation passed.");
    }

    private static UpdateMessage Update(string id, int percentage) =>
        new(id, percentage, PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 0, DateTimeOffset.UtcNow);

    private static async Task TestServiceCapacityShutdownAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        BlockingPublisher publisher = new();
        await using HidppManagerService service = new(publisher,
            provider.GetRequiredService<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>());
        Task? producer = null;
        using ManualResetEventSlim attempting = new();
        try
        {
            HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, Update("in-flight", 1));
            await publisher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (int i = 0; i < 256; i++)
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.INIT,
                    new InitMessage("capacity-" + i, "device", true, DeviceType.Mouse));
            producer = Task.Factory.StartNew(() =>
            {
                attempting.Set();
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.OFFLINE, new DeviceOfflineMessage("beyond-capacity"));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Check(attempting.Wait(TimeSpan.FromSeconds(2)), "Service event producer must attempt admission.");
            await Task.Delay(100);
            Check(!producer.IsCompleted, "The real service must backpressure events beyond its 256-event capacity.");
            Check(publisher.Calls == 1, "Only one publisher call may be in flight despite 256 pending events.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            if (producer != null) await producer.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Check(publisher.Canceled.Task.IsCompletedSuccessfully,
            "Service shutdown must cancel its publisher and release a blocked HID callback without an exception.");
        Console.WriteLine("Real service 256-event capacity and blocked callback shutdown passed.");
    }

    private static ServiceProvider CreateProvider()
    {
        ServiceCollection services = new();
        services.AddMessagePipe();
        services.AddSingleton<IDistributedSubscriber<IPCMessageType, IPCMessage>, InMemoryDistributedSubscriber<IPCMessageType, IPCMessage>>();
        services.AddSingleton<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>, InMemoryDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>();
        return services.BuildServiceProvider();
    }

    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);

    private static async Task ExpectCanceledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Expected lifetime/caller cancellation.");
        }
        catch (OperationCanceledException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class UnexpectedPublisher : IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>
    {
        public ValueTask PublishAsync(IPCMessageRequestType key, IPCRequestMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Native-disabled diagnostics must never publish.");
    }

    private sealed class BlockingPublisher : IDistributedPublisher<IPCMessageType, IPCMessage>
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;

        public async ValueTask PublishAsync(IPCMessageType key, IPCMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
