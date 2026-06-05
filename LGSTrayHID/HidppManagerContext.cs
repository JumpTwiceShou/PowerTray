using LGSTrayHID.HidApi;
using LGSTrayPrimitives.MessageStructs;
using System.Collections.Concurrent;

using static LGSTrayHID.HidApi.HidApi;
using static LGSTrayHID.HidApi.HidApiHotPlug;
using static LGSTrayHID.HidApi.HidApiWinApi;

namespace LGSTrayHID
{
    public sealed class HidppManagerContext
    {
        private const ushort LOGITECH_VENDOR_ID = 0x046D;

        public static readonly HidppManagerContext _instance = new();
        public static HidppManagerContext Instance => _instance;

        private readonly object _sync = new();
        private readonly List<HidppDevices> _sessions = [];
        private readonly HidApiHotPlugEventCallbackFn _hotplugCallback;

        private CancellationToken _cancellationToken;
        private HidHotPlugCallbackHandle _hotplugHandle;
        private int _rediscoverQueued;

        public delegate void HidppDeviceEventHandler(IPCMessageType messageType, IPCMessage message);

        public event HidppDeviceEventHandler? HidppDeviceEvent;

        private unsafe HidppManagerContext()
        {
            _hotplugCallback = HotplugEvent;
        }

        static HidppManagerContext()
        {
            _ = HidInit();
        }

        public void SignalDeviceEvent(IPCMessageType messageType, IPCMessage message)
        {
            HidppDeviceEvent?.Invoke(messageType, message);
        }

        private unsafe int HotplugEvent(HidHotPlugCallbackHandle _, HidDeviceInfo* __, HidApiHotPlugEvent ___, nint ____)
        {
            ScheduleRediscover();
            return 0;
        }

        public void Start(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            RediscoverDevices();

            unsafe
            {
                fixed (int* hotplugHandle = &_hotplugHandle)
                {
                    HidHotplugRegisterCallback(
                        LOGITECH_VENDOR_ID,
                        0x00,
                        HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED | HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT,
                        HidApiHotPlugFlag.NONE,
                        _hotplugCallback,
                        IntPtr.Zero,
                        hotplugHandle
                    );
                }
            }
        }

        public void Stop()
        {
            if (_hotplugHandle != 0)
            {
                HidHotplugDeregisterCallback(_hotplugHandle);
                _hotplugHandle = 0;
            }

            lock (_sync)
            {
                foreach (var session in _sessions)
                {
                    session.Dispose();
                }

                _sessions.Clear();
            }
        }

        private void ScheduleRediscover()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.Exchange(ref _rediscoverQueued, 1) == 1)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, _cancellationToken);
                    RediscoverDevices();
                }
                catch (OperationCanceledException) { }
                finally
                {
                    Interlocked.Exchange(ref _rediscoverQueued, 0);
                }
            });
        }

        public void RediscoverDevices()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var nextSessions = CreateSessions(EnumerateEndpoints());

            lock (_sync)
            {
                foreach (var session in _sessions)
                {
                    session.Dispose();
                }

                _sessions.Clear();
                _sessions.AddRange(nextSessions);
            }

            foreach (var session in nextSessions)
            {
                _ = session.StartAsync();
            }
        }

        private static List<HidppDevices> CreateSessions(IReadOnlyCollection<HidEndpointInfo> endpoints)
        {
            List<HidppDevices> sessions = [];

            foreach (var group in endpoints.GroupBy(x => x.GroupKey))
            {
                var shorts = group
                    .Where(x => x.MessageType == HidppMessageType.SHORT)
                    .OrderBy(x => x.UsagePage)
                    .ThenBy(x => x.Path)
                    .ToList();

                var longs = group
                    .Where(x => x.MessageType == HidppMessageType.LONG)
                    .OrderBy(x => x.UsagePage)
                    .ThenBy(x => x.Path)
                    .ToList();

                if (shorts.Count == 0)
                {
                    continue;
                }

                if (longs.Count > 0)
                {
                    sessions.Add(new HidppDevices(shorts[0], longs[0]));
                    continue;
                }

                foreach (var shortEndpoint in shorts)
                {
                    sessions.Add(new HidppDevices(shortEndpoint, null));
                }
            }

            return sessions;
        }

        private static unsafe List<HidEndpointInfo> EnumerateEndpoints()
        {
            List<HidEndpointInfo> endpoints = [];
            HidDeviceInfo* head = HidEnumerate(LOGITECH_VENDOR_ID, 0x00);

            try
            {
                for (HidDeviceInfo* current = head; current != null; current = current->Next)
                {
                    HidDeviceInfo deviceInfo = *current;
                    var messageType = deviceInfo.GetHidppMessageType();
                    if (messageType is HidppMessageType.NONE or HidppMessageType.VERY_LONG)
                    {
                        continue;
                    }

                    string path = deviceInfo.GetPath();
                    nint dev = HidOpenPath(ref deviceInfo);
                    if (dev == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        _ = HidWinApiGetContainerId(dev, out Guid containerId);
                        endpoints.Add(new HidEndpointInfo(
                            path,
                            containerId,
                            deviceInfo.ProductId,
                            deviceInfo.UsagePage,
                            deviceInfo.Usage,
                            deviceInfo.InterfaceNumber,
                            messageType
                        ));
                    }
                    finally
                    {
                        HidClose(dev);
                    }
                }
            }
            finally
            {
                HidFreeEnumeration(head);
            }

            return endpoints
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        public async Task ForceBatteryUpdates()
        {
            List<HidppDevices> snapshot;
            lock (_sync)
            {
                snapshot = [.. _sessions];
            }

            var tasks = snapshot
                .SelectMany(x => x.DeviceCollection.Values)
                .Select(x => x.UpdateBattery(true));

            await Task.WhenAll(tasks);
        }
    }
}
