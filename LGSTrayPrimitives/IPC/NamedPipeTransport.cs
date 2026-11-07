using MessagePack;
using MessagePipe;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;

namespace LGSTrayPrimitives.IPC;

// These pipes are directional: publishers never start an unused receive loop.
// Both ends must use this framing; HMAC validation remains at the message handler.
internal static class NamedPipeFraming
{
    internal const int MaximumPayloadLength = 4 * 1024 * 1024;

    internal static async ValueTask<byte[]?> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[sizeof(int)];
        int first = await stream.ReadAsync(header, token).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        // ReadExactlyAsync throws on a truncated frame instead of spinning on EOF.
        await stream.ReadExactlyAsync(header.AsMemory(first), token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumPayloadLength)
        {
            throw new InvalidDataException("Invalid PowerTray IPC frame length.");
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return payload;
    }

    internal static byte[] Encode<TKey, TMessage>(TKey key, TMessage message)
    {
        byte[] payload = MessagePackSerializer.Serialize((key, message));
        if (payload.Length > MaximumPayloadLength)
        {
            throw new InvalidDataException("PowerTray IPC frame is too large.");
        }
        byte[] frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, sizeof(int));
        return frame;
    }
}

internal sealed class NamedPipePublisher<TKey, TMessage> : IDistributedPublisher<TKey, TMessage>, IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Task _loop;
    private int _disposed;

    internal NamedPipePublisher(string pipeName)
    {
        _loop = Task.Run(() => RunAsync(pipeName, _stop.Token));
    }

    public ValueTask PublishAsync(TKey key, TMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        return _outgoing.Writer.WriteAsync(NamedPipeFraming.Encode(key, message), cancellationToken);
    }

    private async Task RunAsync(string pipeName, CancellationToken token)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            await foreach (byte[] frame in _outgoing.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (pipe == null)
                        {
                            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                            await pipe.ConnectAsync(token).ConfigureAwait(false);
                        }
                        await pipe.WriteAsync(frame, token).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        pipe?.Dispose();
                        pipe = null;
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // A dead sender must reject new work and release blocked writers.
            // Dispose still completes normally so the other endpoint is cleaned up.
            _outgoing.Writer.TryComplete(ex);
            Debug.WriteLine($"PowerTray IPC sender stopped: {ex.GetType().Name}");
        }
        finally
        {
            _outgoing.Writer.TryComplete();
            pipe?.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _outgoing.Writer.TryComplete();
        _stop.Cancel();
        try { await _loop.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}

internal sealed class NamedPipeReceiver<TKey, TMessage> : IDisposable, IAsyncDisposable where TKey : notnull
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _disposed;

    internal NamedPipeReceiver(string pipeName, IAsyncPublisher<TKey, TMessage> publisher)
    {
        _loop = Task.Run(() => RunAsync(pipeName, publisher, _stop.Token));
    }

    private static async Task RunAsync(string pipeName, IAsyncPublisher<TKey, TMessage> publisher, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeServerStream pipe = new(pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    while (await NamedPipeFraming.ReadAsync(pipe, token).ConfigureAwait(false) is { } payload)
                    {
                        try
                        {
                            var (key, message) = MessagePackSerializer.Deserialize<(TKey, TMessage)>(payload);
                            await publisher.PublishAsync(key, message, token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                        {
                            Debug.WriteLine($"PowerTray IPC message rejected: {ex.GetType().Name}");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // Includes a partial frame followed by EOF. Dispose, then accept a new peer.
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { await _loop.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
