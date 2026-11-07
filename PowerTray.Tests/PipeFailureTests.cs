using LGSTrayPrimitives.IPC;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;

internal static class PipeFailureTests
{
    internal static async Task RunAsync()
    {
        await TestDeniedPublisherAsync(restore: true);
        await TestDeniedPublisherAsync(restore: false);
        await TestTerminalPublisherAsync();
        await TestSubscriberCancellationAsync();
        Console.WriteLine("Pipe permission recovery, denied shutdown, terminal faults, and subscriber cancellation passed.");
    }

    private static async Task TestDeniedPublisherAsync(bool restore)
    {
        string name = "PowerTray.Tests.Denied." + Guid.NewGuid().ToString("N");
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        using NamedPipeServerStream denied = NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        // Verify the fixture actually denies this user before exercising retries.
        using (NamedPipeClientStream probe = new(".", name, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            try
            {
                await probe.ConnectAsync(timeout.Token);
                throw new InvalidOperationException("The pipe ACL fixture must deny this process access.");
            }
            catch (UnauthorizedAccessException) { }
        }
        await using NamedPipePublisher<int, string> publisher = new(name);
        await publisher.PublishAsync(1, "survives-permission-change", timeout.Token);
        await Task.Delay(350, timeout.Token);
        Task loop = (Task)typeof(NamedPipePublisher<int, string>)
            .GetField("_loop", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(publisher)!;
        Check(!loop.IsCompleted, "Access denial must not permanently kill the sender.");
        if (restore)
        {
            denied.Dispose();
            using NamedPipeServerStream replacement = new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await replacement.WaitForConnectionAsync(timeout.Token);
            byte[]? payload = await NamedPipeFraming.ReadAsync(replacement, timeout.Token);
            Check(MessagePack.MessagePackSerializer.Deserialize<(int, string)>(payload!) == (1, "survives-permission-change"),
                "The same publisher must reconnect and deliver its retained frame after permission recovery.");
        }
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Check(loop.IsCompletedSuccessfully, "Disposal must cancel denied connection retries without rethrowing old faults.");
    }

    private static async Task TestTerminalPublisherAsync()
    {
        await using NamedPipePublisher<int, string> publisher = new(""); // Invalid name is a terminal configuration fault.
        Task[] producers = Enumerable.Range(0, 300)
            .Select(i => publisher.PublishAsync(i, "terminal").AsTask()).ToArray();
        int rejected = 0;
        foreach (Task producer in producers)
        {
            try { await producer.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (ChannelClosedException ex) when (ex.InnerException is ArgumentException) { rejected++; }
        }
        Check(rejected > 0, "A terminal worker failure must release queued writers with its cause.");
        try
        {
            await publisher.PublishAsync(301, "must-not-accept").AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("A dead sender must reject new work immediately.");
        }
        catch (ChannelClosedException ex) when (ex.InnerException is ArgumentException) { }
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task TestSubscriberCancellationAsync()
    {
        string name = "PowerTray.Tests.HandlerCancellation." + Guid.NewGuid().ToString("N");
        ServiceCollection services = new();
        services.AddMessagePipe();
        await using ServiceProvider provider = services.BuildServiceProvider();
        TaskCompletionSource<int> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = provider.GetRequiredService<IAsyncSubscriber<int, int>>().Subscribe(1, (message, _) =>
        {
            if (message == 1) throw new OperationCanceledException("handler's own cancellation");
            received.TrySetResult(message);
            return ValueTask.CompletedTask;
        });
        await using NamedPipeReceiver<int, int> receiver = new(name, provider.GetRequiredService<IAsyncPublisher<int, int>>());
        await using NamedPipePublisher<int, int> publisher = new(name);
        await publisher.PublishAsync(1, 1);
        await publisher.PublishAsync(1, 2);
        Check(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)) == 2,
            "A subscriber's unrelated cancellation must not stop the pipe receiver.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
