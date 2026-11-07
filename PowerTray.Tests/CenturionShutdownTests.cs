using LGSTrayHID;
using LGSTrayHID.HidApi;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;

internal static class CenturionShutdownTests
{
    internal static async Task RunAsync()
    {
        foreach (bool bridge in new[] { false, true })
        {
            await TestDisposalAsync(bridge, afterAck: false);
            await TestClosedChannelAsync(bridge);
            await TestSuccessfulResponseAsync(bridge);
        }
        await TestDisposalAsync(bridge: true, afterAck: true);
        await TestProbeDisposalAsync();
        Console.WriteLine("Centurion shutdown, terminal channel closure, and successful response tests passed.");
    }

    private static async Task TestDisposalAsync(bool bridge, bool afterAck)
    {
        await using HidppDevices device = CreateDevice();
        if (afterAck) Responses(device).Writer.TryWrite(Frame(device, [5, 0x12]));
        Task<byte[]?> request = BeginRequest(device, bridge);
        device.TrackBackgroundTask(request);
        Check(!request.IsCompleted, "Request must be waiting for a response before disposal.");
        Stopwatch timer = Stopwatch.StartNew();
        await device.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Check(request.IsCanceled, "Session disposal must cancel the active Centurion request, not retry until its deadline.");
        Check(timer.Elapsed < TimeSpan.FromMilliseconds(750), "Centurion disposal must finish promptly, before the request deadline.");
        Console.WriteLine($"Centurion disposal bridge={bridge} afterAck={afterAck}: {timer.ElapsedMilliseconds}ms");
    }

    private static async Task TestClosedChannelAsync(bool bridge)
    {
        await using HidppDevices device = CreateDevice();
        Task<byte[]?> request = BeginRequest(device, bridge);
        device.TrackBackgroundTask(request);
        Responses(device).Writer.TryComplete();
        try
        {
            await request.WaitAsync(TimeSpan.FromMilliseconds(750));
            throw new InvalidOperationException("A closed response channel must terminate the whole request.");
        }
        catch (ChannelClosedException) { }
    }

    private static async Task TestSuccessfulResponseAsync(bool bridge)
    {
        await using HidppDevices device = CreateDevice();
        Task<byte[]?> request = BeginRequest(device, bridge);
        // Cross a normal 200ms read timeout; it must remain retryable.
        await Task.Delay(250);
        if (bridge)
        {
            Responses(device).Writer.TryWrite(Frame(device, [5, 0x12]));
            Responses(device).Writer.TryWrite(Frame(device, [5, 0x10, 0, 0, 0, 3, 0x02, 0x42]));
        }
        else
        {
            Responses(device).Writer.TryWrite(Frame(device, [3, 0x02, 0x42]));
        }
        byte[]? response = await request.WaitAsync(TimeSpan.FromSeconds(2));
        Check(response is [0x42], "A valid Centurion response must still decode after the lifecycle fix.");
    }

    private static async Task TestProbeDisposalAsync()
    {
        await using HidppDevices device = CreateDevice();
        typeof(HidppDevices).GetField("_centurionReportId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(device, CenturionFrameCodec.AddressedReportId);
        Task probe = (Task)typeof(HidppDevices).GetMethod("ProbeCenturionDeviceAddressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(device, null)!;
        device.TrackBackgroundTask(probe);
        await device.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Check(probe.IsCanceled, "Address probing must propagate session cancellation too.");
    }

    private static HidppDevices CreateDevice() => new(new HidEndpointInfo(
        "test-unopened-device", Guid.NewGuid(), 0x046d, 0x0aba, 0,
        null, null, null, "test-path", "not-opened", 0xff00, 1, 0, HidppMessageType.CENTURION), null);

    private static Task<byte[]?> BeginRequest(HidppDevices device, bool bridge)
    {
        string method = bridge ? "CenturionBridgeRequestAsync" : "CenturionRequestAsync";
        object[] arguments = bridge
            ? [(byte)5, (byte)3, (byte)0, Array.Empty<byte>(), 1500]
            : [(byte)3, (byte)0, Array.Empty<byte>(), 1000];
        return (Task<byte[]?>)typeof(HidppDevices).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(device, arguments)!;
    }

    private static Channel<byte[]> Responses(HidppDevices device) =>
        (Channel<byte[]>)typeof(HidppDevices).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;

    private static byte[] Frame(HidppDevices device, byte[] payload) =>
        CenturionFrameCodec.BuildFrame(device.CenturionReportId, device.CenturionDeviceAddress, payload);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
