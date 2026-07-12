#define PRINT
using System.Threading.Channels;
using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID.HidApi;

public readonly struct HidDevicePtr : IDisposable, IEquatable<HidDevicePtr>
{
    private readonly SafeHidDeviceHandle? _handle;

    private HidDevicePtr(SafeHidDeviceHandle handle)
    {
        _handle = handle;
    }

    internal SafeHidDeviceHandle? SafeHandle => _handle;
    public bool IsInvalid => _handle == null || _handle.IsInvalid || _handle.IsClosed;

    public static implicit operator nint(HidDevicePtr ptr) =>
        ptr._handle == null || ptr._handle.IsClosed ? IntPtr.Zero : ptr._handle.DangerousGetHandle();

    public static implicit operator HidDevicePtr(nint ptr) =>
        ptr == IntPtr.Zero ? default : new HidDevicePtr(new SafeHidDeviceHandle(ptr));

    public Task<int> WriteAsync(byte[] buffer)
    {
        if (_handle == null || _handle.IsInvalid || _handle.IsClosed)
        {
            return Task.FromResult(-1);
        }

        bool addedRef = false;
        try
        {
            _handle.DangerousAddRef(ref addedRef);
            nint raw = _handle.DangerousGetHandle();
#if DEBUG && PRINT
            PrintBuffer($"0x{raw:X} - W", buffer);
#endif
            return Task.FromResult(HidWrite(raw, buffer, (nuint)buffer.Length));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(-1);
        }
        finally
        {
            if (addedRef)
            {
                _handle.DangerousRelease();
            }
        }
    }

    public int Read(byte[] buffer, int count, int timeout)
    {
        if (_handle == null || _handle.IsInvalid || _handle.IsClosed)
        {
            return -1;
        }

        bool addedRef = false;
        try
        {
            _handle.DangerousAddRef(ref addedRef);
            nint raw = _handle.DangerousGetHandle();
            int ret = HidReadTimeOut(raw, buffer, (nuint)count, timeout);
#if DEBUG && PRINT
            PrintBuffer($"0x{raw:X} - R", buffer, ret < 1);
#endif
            return ret;
        }
        catch (ObjectDisposedException)
        {
            return -1;
        }
        finally
        {
            if (addedRef)
            {
                _handle.DangerousRelease();
            }
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
    }

    public bool Equals(HidDevicePtr other) => (nint)this == (nint)other;
    public override bool Equals(object? obj) => obj is HidDevicePtr other && Equals(other);
    public override int GetHashCode() => ((nint)this).GetHashCode();
    public static bool operator ==(HidDevicePtr left, HidDevicePtr right) => left.Equals(right);
    public static bool operator !=(HidDevicePtr left, HidDevicePtr right) => !left.Equals(right);

#if DEBUG && PRINT
    private static int count;
    private static readonly Channel<string> DebugChannel = Channel.CreateUnbounded<string>();

    static HidDevicePtr()
    {
        Thread thread = new(async () =>
        {
            await foreach (string value in DebugChannel.Reader.ReadAllAsync())
            {
                Console.WriteLine(value);
            }
        })
        {
            IsBackground = true,
            Name = "PowerTray HID debug output",
        };
        thread.Start();
    }

    private static void PrintBuffer(string prefix, byte[] buffer, bool ignore = false)
    {
        if (ignore)
        {
            return;
        }

        string arr = string.Join(" ", Array.ConvertAll(buffer, x => x.ToString("X02")));
        DebugChannel.Writer.TryWrite($"{Interlocked.Increment(ref count):d04} - {prefix}: {arr}");
    }
#endif
}
