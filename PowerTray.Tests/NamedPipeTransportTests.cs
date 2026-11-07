using LGSTrayPrimitives.IPC;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using System.IO.Pipes;

internal static class NamedPipeTransportTests
{
    internal static async Task RunAsync()
    {
        await TestFramingAsync();
        await TestReceiverReconnectAsync();
        await TestPublisherReconnectAsync();
        await TestPendingShutdownAsync();
        Console.WriteLine("Named-pipe framing, disconnect, reconnect, and shutdown tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task TestFramingAsync()
    {
        byte[] first = NamedPipeFraming.Encode(1, "one");
        byte[] second = NamedPipeFraming.Encode(2, "two");
        using ChunkedStream stream = new([.. first, .. second], chunkSize: 1);
        byte[]? payload = await NamedPipeFraming.ReadAsync(stream, default);
        Check(MessagePack.MessagePackSerializer.Deserialize<(int, string)>(payload!) == (1, "one"), "Split header/payload must decode.");
        payload = await NamedPipeFraming.ReadAsync(stream, default);
        Check(MessagePack.MessagePackSerializer.Deserialize<(int, string)>(payload!) == (2, "two"), "Adjacent frames must remain separate.");
        Check(await NamedPipeFraming.ReadAsync(stream, default) == null, "Boundary EOF must close the connection.");

        foreach (int length in new[] { 1, 3, first.Length - 1 })
        {
            using ChunkedStream truncated = new(first[..length], chunkSize: 1);
            try
            {
                await NamedPipeFraming.ReadAsync(truncated, default);
                throw new InvalidOperationException("Truncated header/payload must throw at EOF.");
            }
            catch (EndOfStreamException) { }
            Check(truncated.ReadCount <= length + 1, "EOF must not cause repeated reads/spin.");
        }

        foreach (int length in new[] { 0, -1, NamedPipeFraming.MaximumPayloadLength + 1 })
        {
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, length);
            using MemoryStream invalid = new(header);
            try
            {
                await NamedPipeFraming.ReadAsync(invalid, default);
                throw new InvalidOperationException("Invalid length must be rejected before allocation.");
            }
            catch (InvalidDataException) { }
        }
    }

    private static async Task TestReceiverReconnectAsync()
    {
        string pipeName = "PowerTray.Tests.Receiver." + Guid.NewGuid().ToString("N");
        ServiceCollection services = new();
        services.AddMessagePipe();
        services.AddSingleton<IDistributedSubscriber<IPCMessageType, IPCMessage>, InMemoryDistributedSubscriber<IPCMessageType, IPCMessage>>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        TaskCompletionSource<IPCMessage> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using IAsyncDisposable subscription = await provider.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>()
            .SubscribeAsync(IPCMessageType.HEARTBEAT, message => received.TrySetResult(message));
        await using NamedPipeReceiver<IPCMessageType, IPCMessage> receiver = new(pipeName,
            provider.GetRequiredService<IAsyncPublisher<IPCMessageType, IPCMessage>>());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        // Disconnect at the frame boundary, in the header, and in the payload.
        foreach (byte[] partial in new byte[][] { [], [10], [10, 0, 0, 0, 1], [255, 255, 255, 127] })
        {
            using NamedPipeClientStream peer = new(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await peer.ConnectAsync(timeout.Token);
            if (partial.Length != 0) await peer.WriteAsync(partial, timeout.Token);
        }
        await using NamedPipePublisher<IPCMessageType, IPCMessage> publisher = new(pipeName);
        await publisher.PublishAsync(IPCMessageType.HEARTBEAT, new HeartbeatMessage(42, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "running"));
        Check(await received.Task.WaitAsync(timeout.Token) is HeartbeatMessage, "Receiver must accept a fresh peer after EOF/truncation/invalid framing.");
    }

    private static async Task TestPublisherReconnectAsync()
    {
        string pipeName = "PowerTray.Tests.Publisher." + Guid.NewGuid().ToString("N");
        await using NamedPipePublisher<int, string> publisher = new(pipeName);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using (NamedPipeServerStream first = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        {
            await publisher.PublishAsync(1, "before");
            await first.WaitForConnectionAsync(timeout.Token);
            Check(await NamedPipeFraming.ReadAsync(first, timeout.Token) != null, "Initial publish must arrive.");
        }
        using NamedPipeServerStream replacement = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await publisher.PublishAsync(2, "after", timeout.Token);
        await replacement.WaitForConnectionAsync(timeout.Token);
        byte[]? payload = await NamedPipeFraming.ReadAsync(replacement, timeout.Token);
        Check(MessagePack.MessagePackSerializer.Deserialize<(int, string)>(payload!) == (2, "after"), "Existing publisher must reconnect when its server restarts.");
    }

    private static async Task TestPendingShutdownAsync()
    {
        string pipeName = "PowerTray.Tests.Shutdown." + Guid.NewGuid().ToString("N");
        NamedPipePublisher<int, string> publisher = new(pipeName);
        await publisher.PublishAsync(1, "no receiver");
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        ServiceCollection services = new();
        services.AddMessagePipe();
        await using ServiceProvider provider = services.BuildServiceProvider();
        NamedPipeReceiver<int, string> receiver = new(pipeName, provider.GetRequiredService<IAsyncPublisher<int, string>>());
        await receiver.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        // A connected receiver blocked in a partial header must cancel as well.
        receiver = new(pipeName, provider.GetRequiredService<IAsyncPublisher<int, string>>());
        using NamedPipeClientStream peer = new(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await peer.ConnectAsync(timeout.Token);
        await peer.WriteAsync(new byte[] { 1 }, timeout.Token);
        await receiver.DisposeAsync().AsTask().WaitAsync(timeout.Token);
    }

    private sealed class ChunkedStream : MemoryStream
    {
        private readonly int _chunkSize;
        internal int ReadCount { get; private set; }

        internal ChunkedStream(byte[] bytes, int chunkSize) : base(bytes) => _chunkSize = chunkSize;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++ReadCount > Length + 2) throw new InvalidOperationException("Read loop continued after EOF.");
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, _chunkSize)], cancellationToken);
        }
    }
}
