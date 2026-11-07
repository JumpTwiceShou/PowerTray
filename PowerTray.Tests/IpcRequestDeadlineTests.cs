using LGSTrayCore.Managers;
using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayUI;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;

internal static class IpcRequestDeadlineTests
{
    internal static async Task RunAsync()
    {
        IpcSessionContext.CreateAndSetToken();
        await TestHealthAdmissionDeadlineAsync();
        await TestFullQueueDeadlinesAsync();
        await TestRediscoverCallerCancellationAsync();
        await TestDiagnosticsCancellationAsync();
        await TestSubscriptionWaitAsync(dispose: false);
        await TestSubscriptionWaitAsync(dispose: true);
        await TestCombinedDiagnosticsBudgetAsync();
        await TestManagerStopAsync();
        await TestDiagnosticsSuccessAsync();
        Console.WriteLine("IPC request admission deadlines, cancellation, and successful response tests passed.");
    }

    private static async Task TestHealthAdmissionDeadlineAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = await CreateFullPublisherAsync();
        using LGSTrayHIDManager manager = CreateManager(provider, publisher);
        using CancellationTokenSource daemon = new();
        SetDaemon(manager, daemon);
        Task health = manager.CheckHealthAsync(CancellationToken.None);
        try
        {
            await health.WaitAsync(TimeSpan.FromSeconds(4));
            Check(daemon.IsCancellationRequested, "A blocked health publish must recover the helper instead of stalling future health checks.");
        }
        finally
        {
            await publisher.DisposeAsync();
            await ObserveAsync(health);
        }
    }

    private static async Task TestFullQueueDeadlinesAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = await CreateFullPublisherAsync();
        using NativeDiagnosticsClient diagnostics = new(Subscriber(provider), publisher);
        using LGSTrayHIDManager manager = CreateManager(provider, publisher);
        using CancellationTokenSource daemon = new();
        SetDaemon(manager, daemon);
        Stopwatch timer = Stopwatch.StartNew();
        Task<NativeDiagnosticsResponseMessage?> diagnosticRequest = diagnostics.RequestAsync(TimeSpan.FromMilliseconds(150));
        Task rediscover = manager.RediscoverDevicesAsync(CancellationToken.None);
        try
        {
            await Task.Delay(600);
            bool diagnosticsTimedOut = diagnosticRequest.IsCompletedSuccessfully && diagnosticRequest.Result == null;
            bool rediscoverTimedOut = false;
            try
            {
                await rediscover.WaitAsync(TimeSpan.FromSeconds(17));
            }
            catch (TimeoutException)
            {
                rediscoverTimedOut = rediscover.IsFaulted && rediscover.Exception?.InnerException is TimeoutException;
            }
            Check(diagnosticsTimedOut, "Diagnostics deadline must cover waiting for space in the IPC queue.");
            Check(rediscoverTimedOut, "Rediscover's 15-second deadline must cover waiting for space in the IPC queue.");
            Check(daemon.IsCancellationRequested, "A rediscover deadline must retain helper restart behavior.");
            Check(PendingCount(diagnostics, "_pending") == 0 && PendingCount(manager, "_pendingRediscover") == 0,
                "Timed-out requests must be removed from pending dictionaries.");
            Console.WriteLine($"Full IPC queue deadlines: diagnostics <=600ms; rediscover {timer.ElapsedMilliseconds}ms; recovery signaled.");
        }
        finally
        {
            await publisher.DisposeAsync();
            await ObserveAsync(diagnosticRequest);
            await ObserveAsync(rediscover);
        }
    }

    private static async Task TestRediscoverCallerCancellationAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = await CreateFullPublisherAsync();
        using LGSTrayHIDManager manager = CreateManager(provider, publisher);
        using CancellationTokenSource daemon = new();
        SetDaemon(manager, daemon);
        using CancellationTokenSource caller = new();
        Task request = manager.RediscoverDevicesAsync(caller.Token);
        caller.Cancel();
        try
        {
            await request.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Caller cancellation must cancel a blocked rediscover request.");
        }
        catch (OperationCanceledException) { }
        Check(!daemon.IsCancellationRequested, "Caller cancellation must not be treated as a timeout/restart.");
        Check(PendingCount(manager, "_pendingRediscover") == 0, "Canceled rediscover must release pending state.");
        using CancellationTokenSource healthCaller = new();
        Task health = manager.CheckHealthAsync(healthCaller.Token);
        healthCaller.Cancel();
        await AssertCanceledAsync(health);
        Check(!daemon.IsCancellationRequested, "Canceling a health request must not trigger helper recovery.");
    }

    private static async Task TestDiagnosticsSuccessAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        IAsyncPublisher<IPCMessageType, IPCMessage> responses = provider.GetRequiredService<IAsyncPublisher<IPCMessageType, IPCMessage>>();
        int calls = 0;
        CallbackPublisher publisher = new(async (_, message, token) =>
        {
            if (++calls == 1) return; // First request times out after admission.
            NativeDiagnosticsRequestMessage request = (NativeDiagnosticsRequestMessage)message;
            NativeDiagnosticsResponseMessage response = new(request.requestId, "{}", "test response");
            IpcSessionContext.Sign(IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE, response);
            await responses.PublishAsync(IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE, response, token);
        });
        using NativeDiagnosticsClient client = new(Subscriber(provider), publisher);
        Check(await client.RequestAsync(TimeSpan.FromMilliseconds(100)) == null, "Missing responses must still time out.");
        NativeDiagnosticsResponseMessage? result = await client.RequestAsync(TimeSpan.FromSeconds(2));
        Check(result != null, "A valid authenticated diagnostics response must still be delivered after a prior timeout.");
        Check(PendingCount(client, "_pending") == 0, "Successful diagnostics must release pending state.");
    }

    private static async Task TestDiagnosticsCancellationAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = await CreateFullPublisherAsync();
        foreach (bool dispose in new[] { false, true })
        {
            using NativeDiagnosticsClient client = new(Subscriber(provider), publisher);
            using CancellationTokenSource caller = new();
            Task request = client.RequestAsync(TimeSpan.FromSeconds(30), caller.Token);
            if (dispose) client.Dispose();
            else caller.Cancel();
            await AssertCanceledAsync(request);
            Check(PendingCount(client, "_pending") == 0, "Canceled diagnostics must release pending state.");
        }
    }

    private static async Task TestSubscriptionWaitAsync(bool dispose)
    {
        await using ServiceProvider provider = CreateProvider();
        int published = 0;
        using NativeDiagnosticsClient client = new(Subscriber(provider), new CallbackPublisher((_, _, _) =>
        {
            published++;
            return ValueTask.CompletedTask;
        }));
        SemaphoreSlim gate = (SemaphoreSlim)typeof(NativeDiagnosticsClient)
            .GetField("_subscriptionLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        await gate.WaitAsync();
        try
        {
            Task<NativeDiagnosticsResponseMessage?> request = client.RequestAsync(TimeSpan.FromMilliseconds(100));
            if (dispose)
            {
                client.Dispose();
                await AssertCanceledAsync(request);
            }
            else
            {
                Check(await request.WaitAsync(TimeSpan.FromSeconds(2)) == null, "Deadline must cover waiting for subscription setup.");
            }
            Check(published == 0, "Canceled subscription setup must not send a request.");
        }
        finally { gate.Release(); }
    }

    private static async Task TestCombinedDiagnosticsBudgetAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        using NativeDiagnosticsClient client = new(Subscriber(provider), new CallbackPublisher(async (_, _, token) =>
            await Task.Delay(400, token)));
        Task<NativeDiagnosticsResponseMessage?> request = client.RequestAsync(TimeSpan.FromMilliseconds(600));
        Check(await request.WaitAsync(TimeSpan.FromMilliseconds(900)) == null,
            "Response waiting must use the remaining operation budget, not a fresh timeout after admission.");
    }

    private static async Task TestManagerStopAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = await CreateFullPublisherAsync();
        using LGSTrayHIDManager manager = CreateManager(provider, publisher);
        Task request = manager.RediscoverDevicesAsync(CancellationToken.None);
        Task health = manager.CheckHealthAsync(CancellationToken.None);
        await manager.StopAsync(CancellationToken.None);
        await AssertCanceledAsync(request);
        await AssertCanceledAsync(health);
        Check(PendingCount(manager, "_pendingRediscover") == 0, "Stopping the manager must release blocked requests.");
    }

    private static async Task AssertCanceledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Expected operation cancellation.");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage>> CreateFullPublisherAsync()
    {
        NamedPipePublisher<IPCMessageRequestType, IPCRequestMessage> publisher = new("PowerTray.Tests.Deadline." + Guid.NewGuid().ToString("N"));
        try
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            // One in-flight connection attempt plus all 256 bounded queue slots.
            for (int i = 0; i < 257; i++)
                await publisher.PublishAsync(IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST,
                    new NativeDiagnosticsRequestMessage("fill-" + i), deadline.Token);
            return publisher;
        }
        catch
        {
            await publisher.DisposeAsync();
            throw;
        }
    }

    private static ServiceProvider CreateProvider()
    {
        ServiceCollection services = new();
        services.AddMessagePipe();
        services.AddSingleton<IDistributedSubscriber<IPCMessageType, IPCMessage>, InMemoryDistributedSubscriber<IPCMessageType, IPCMessage>>();
        return services.BuildServiceProvider();
    }

    private static IDistributedSubscriber<IPCMessageType, IPCMessage> Subscriber(ServiceProvider provider) =>
        provider.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>();

    // No StartAsync call: tests never launch a real HID helper.
    private static LGSTrayHIDManager CreateManager(ServiceProvider provider, IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> publisher) =>
        new(Subscriber(provider), publisher, provider.GetRequiredService<IPublisher<IPCMessage>>(), new NativeBackendStatus());

    private static void SetDaemon(LGSTrayHIDManager manager, CancellationTokenSource daemon) =>
        typeof(LGSTrayHIDManager).GetField("_daemonCts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, daemon);

    private static int PendingCount(object owner, string field)
    {
        object pending = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        return (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException or TimeoutException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CallbackPublisher(Func<IPCMessageRequestType, IPCRequestMessage, CancellationToken, ValueTask> publish)
        : IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>
    {
        public ValueTask PublishAsync(IPCMessageRequestType key, IPCRequestMessage message, CancellationToken cancellationToken = default) =>
            publish(key, message, cancellationToken);
    }
}
