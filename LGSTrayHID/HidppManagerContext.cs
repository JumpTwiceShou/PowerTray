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
        private static readonly int[] HotplugArrivalRediscoverDelaysMs = [50, 300, 1000];

        public static readonly HidppManagerContext _instance = new();
        public static HidppManagerContext Instance => _instance;

        private readonly object _sync = new();
        private readonly List<HidppDevices> _sessions = [];
        private readonly HidApiHotPlugEventCallbackFn _hotplugCallback;
        private readonly SemaphoreSlim _rediscoverLock = new(1, 1);

        private CancellationToken _cancellationToken;
        private HidHotPlugCallbackHandle _hotplugHandle;
        private int _rediscoverQueued;
        private int _hotplugArrivalRediscoverQueued;

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

        private unsafe int HotplugEvent(HidHotPlugCallbackHandle _, HidDeviceInfo* device, HidApiHotPlugEvent hotplugEvent, nint __)
        {
            bool deviceArrived = (hotplugEvent & HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED) != 0;
            if ((hotplugEvent & HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT) != 0 && device != null)
            {
                HidDeviceInfo deviceInfo = *device;
                string pathHash = NativeDiagnosticsStore.HashForDiagnostics(deviceInfo.GetPath());
                SignalSessionsOfflineForEndpoint(pathHash, "hotplugLeft");
            }

            if (deviceArrived)
            {
                ScheduleHotplugArrivalRediscover();
            }
            else
            {
                ScheduleRediscover(250);
            }

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

        private void ScheduleRediscover(int delayMs = 1000)
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
                    await Task.Delay(delayMs, _cancellationToken);
                    RediscoverDevices();
                }
                catch (OperationCanceledException) { }
                finally
                {
                    Interlocked.Exchange(ref _rediscoverQueued, 0);
                }
            });
        }

        private void ScheduleHotplugArrivalRediscover()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.Exchange(ref _hotplugArrivalRediscoverQueued, 1) == 1)
            {
                return;
            }

            NativeDiagnosticsStore.AddEvent("Hotplug arrival detected; scheduling fast rediscover burst");

            _ = Task.Run(async () =>
            {
                int previousDelayMs = 0;
                try
                {
                    foreach (int delayMs in HotplugArrivalRediscoverDelaysMs)
                    {
                        int waitMs = Math.Max(0, delayMs - previousDelayMs);
                        previousDelayMs = delayMs;
                        await Task.Delay(waitMs, _cancellationToken);
                        RediscoverDevices();
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    Interlocked.Exchange(ref _hotplugArrivalRediscoverQueued, 0);
                }
            });
        }

        private void SignalSessionsOfflineForEndpoint(string pathHash, string reason)
        {
            if (string.IsNullOrWhiteSpace(pathHash))
            {
                return;
            }

            lock (_sync)
            {
                foreach (var session in _sessions)
                {
                    if (session.MatchesEndpointPathHash(pathHash))
                    {
                        session.SignalKnownDevicesOffline(reason);
                    }
                }
            }
        }

        public void RediscoverDevices()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!_rediscoverLock.Wait(0))
            {
                NativeDiagnosticsStore.AddEvent("Rediscover skipped; discovery already running");
                return;
            }

            try
            {
                IReadOnlyCollection<HidEndpointInfo> endpoints = EnumerateEndpoints();
                NativeDiagnosticsStore.BeginDiscovery(endpoints);
                var nextSessions = CreateSessions(endpoints);
                HashSet<string> nextSessionKeys = nextSessions
                    .Select(x => x.EndpointIdentityKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                lock (_sync)
                {
                    foreach (var session in _sessions)
                    {
                        if (!nextSessionKeys.Contains(session.EndpointIdentityKey))
                        {
                            session.SignalKnownDevicesOffline("endpointRemoved");
                        }

                        session.Dispose();
                    }

                    _sessions.Clear();
                    _sessions.AddRange(nextSessions);
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var session in nextSessions)
                        {
                            if (_cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            await session.StartAsync();

                            try
                            {
                                await Task.Delay(150, _cancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }

                        SignalDeviceEvent(
                            IPCMessageType.NATIVE_DIAGNOSTICS_RESPONSE,
                            new NativeDiagnosticsResponseMessage(
                                NativeDiagnosticsResponseMessage.LatestSnapshotRequestId,
                                NativeDiagnosticsStore.GetJson(),
                                NativeDiagnosticsStore.GetSummary()
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        NativeDiagnosticsStore.AddEvent($"Rediscover failed: {ex.GetType().Name}");
                    }
                    finally
                    {
                        _rediscoverLock.Release();
                    }
                });
            }
            catch
            {
                _rediscoverLock.Release();
                throw;
            }
        }

        private static List<HidppDevices> CreateSessions(IReadOnlyCollection<HidEndpointInfo> endpoints)
        {
            List<HidppDevices> sessions = [];

            foreach (var group in endpoints.GroupBy(x => x.GroupKey))
            {
                var logitechEndpoints = group
                    .Where(x => x.VendorId == LOGITECH_VENDOR_ID && x.OpenStatus.Equals("opened", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (logitechEndpoints.Count == 0)
                {
                    continue;
                }

                var centurions = logitechEndpoints
                    .Where(x => x.MessageType == HidppMessageType.CENTURION && KnownLogitechDevices.IsCenturionProduct(x.ProductId))
                    .OrderBy(x => x.UsagePage)
                    .ThenBy(x => x.Path)
                    .ToList();

                if (centurions.Count > 0)
                {
                    sessions.Add(new HidppDevices(centurions[0], null));
                    continue;
                }

                var shorts = logitechEndpoints
                    .Where(x => x.MessageType == HidppMessageType.SHORT)
                    .OrderBy(x => x.UsagePage)
                    .ThenBy(x => x.Path)
                    .ToList();

                var longs = logitechEndpoints
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
            HidDeviceInfo* head = HidEnumerate(0x00, 0x00);

            try
            {
                for (HidDeviceInfo* current = head; current != null; current = current->Next)
                {
                    HidDeviceInfo deviceInfo = *current;
                    bool isLogitech = deviceInfo.VendorId == LOGITECH_VENDOR_ID;
                    var messageType = isLogitech ? deviceInfo.GetHidppMessageType() : HidppMessageType.NONE;

                    string path = deviceInfo.GetPath();
                    if (!isLogitech)
                    {
                        endpoints.Add(new HidEndpointInfo(
                            path,
                            Guid.Empty,
                            deviceInfo.VendorId,
                            deviceInfo.ProductId,
                            deviceInfo.ReleaseNumber,
                            deviceInfo.GetManufacturerString(),
                            deviceInfo.GetProductString(),
                            NativeDiagnosticsStore.HashForDiagnostics(deviceInfo.GetSerialNumber()),
                            NativeDiagnosticsStore.HashForDiagnostics(path),
                            "notProbedNonLogitech",
                            deviceInfo.UsagePage,
                            deviceInfo.Usage,
                            deviceInfo.InterfaceNumber,
                            messageType
                        ));
                        continue;
                    }

                    nint dev = HidOpenPath(ref deviceInfo);
                    if (dev == IntPtr.Zero)
                    {
                        endpoints.Add(new HidEndpointInfo(
                            path,
                            Guid.Empty,
                            deviceInfo.VendorId,
                            deviceInfo.ProductId,
                            deviceInfo.ReleaseNumber,
                            deviceInfo.GetManufacturerString(),
                            deviceInfo.GetProductString(),
                            NativeDiagnosticsStore.HashForDiagnostics(deviceInfo.GetSerialNumber()),
                            NativeDiagnosticsStore.HashForDiagnostics(path),
                            "openFailed",
                            deviceInfo.UsagePage,
                            deviceInfo.Usage,
                            deviceInfo.InterfaceNumber,
                            messageType
                        ));
                        continue;
                    }

                    try
                    {
                        _ = HidWinApiGetContainerId(dev, out Guid containerId);
                        endpoints.Add(new HidEndpointInfo(
                            path,
                            containerId,
                            deviceInfo.VendorId,
                            deviceInfo.ProductId,
                            deviceInfo.ReleaseNumber,
                            deviceInfo.GetManufacturerString(),
                            deviceInfo.GetProductString(),
                            NativeDiagnosticsStore.HashForDiagnostics(deviceInfo.GetSerialNumber()),
                            NativeDiagnosticsStore.HashForDiagnostics(path),
                            "opened",
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
