using LGSTrayHID.HidApi;
using LGSTrayPrimitives.MessageStructs;

using static LGSTrayHID.HidApi.HidApi;
using static LGSTrayHID.HidApi.HidApiHotPlug;
using static LGSTrayHID.HidApi.HidApiWinApi;

namespace LGSTrayHID;

public sealed class HidppManagerContext
{
    private const ushort LOGITECH_VENDOR_ID = 0x046D;
    private const int HOTPLUG_LEFT_REDISCOVER_DELAY_MS = 500;
    private const int HOTPLUG_OFFLINE_GRACE_MS = 3000;
    private const int REDISCOVER_FOLLOW_UP_DELAY_MS = 250;
    private static readonly int[] HotplugArrivalRediscoverDelaysMs = [50, 300, 1000];

    private static readonly HidppManagerContext InstanceValue = new();
    public static HidppManagerContext Instance => InstanceValue;

    private readonly object _sync = new();
    private readonly object _backgroundSync = new();
    private readonly List<HidppDevices> _sessions = [];
    private readonly HashSet<Task> _backgroundTasks = [];
    private readonly DeferredOfflineGate _offlineGate = new(
        NativeDiagnosticsStore.AddEvent,
        NativeDiagnosticsStore.HashForDiagnostics
    );
    private readonly HidApiHotPlugEventCallbackFn _hotplugCallback;
    private readonly SemaphoreSlim _rediscoverLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private HidHotPlugCallbackHandle _hotplugHandle;
    private int _rediscoverQueued;
    private int _rediscoverRequestedWhileRunning;
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

    private CancellationToken LifetimeToken => _lifetimeCts?.Token ?? CancellationToken.None;

    public void SignalDeviceEvent(IPCMessageType messageType, IPCMessage message)
    {
        switch (messageType)
        {
            case IPCMessageType.INIT when message is InitMessage initMessage:
                _offlineGate.Cancel(initMessage.deviceId);
                break;
            case IPCMessageType.UPDATE when message is UpdateMessage updateMessage:
                _offlineGate.Cancel(updateMessage.deviceId);
                break;
            case IPCMessageType.OFFLINE when message is DeviceOfflineMessage offlineMessage:
                if (_offlineGate.TryDefer(offlineMessage, EmitOffline))
                {
                    return;
                }
                break;
        }

        HidppDeviceEvent?.Invoke(messageType, message);
    }

    private void EmitOffline(DeviceOfflineMessage offlineMessage)
    {
        HidppDeviceEvent?.Invoke(IPCMessageType.OFFLINE, offlineMessage);
    }

    private unsafe int HotplugEvent(HidHotPlugCallbackHandle _, HidDeviceInfo* device, HidApiHotPlugEvent hotplugEvent, nint __)
    {
        if (LifetimeToken.IsCancellationRequested)
        {
            return 0;
        }

        bool deviceArrived = (hotplugEvent & HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED) != 0;
        bool deviceLeft = (hotplugEvent & HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT) != 0;
        if (deviceLeft)
        {
            _offlineGate.BeginDeferral("hotplugLeft", TimeSpan.FromMilliseconds(HOTPLUG_OFFLINE_GRACE_MS));

            if (device != null)
            {
                HidDeviceInfo deviceInfo = *device;
                string pathHash = NativeDiagnosticsStore.HashForDiagnostics(deviceInfo.GetPath());
                NativeDiagnosticsStore.AddEvent($"Hotplug left detected for endpoint pathHash={pathHash}; offline signals deferred");
            }
            else
            {
                NativeDiagnosticsStore.AddEvent("Hotplug left detected without endpoint details; offline signals deferred");
            }
        }

        if (deviceArrived)
        {
            ScheduleHotplugArrivalRediscover();
        }
        else if (deviceLeft)
        {
            ScheduleRediscover(HOTPLUG_LEFT_REDISCOVER_DELAY_MS, "hotplugLeft");
        }
        else
        {
            ScheduleRediscover(250, "hotplug");
        }

        return 0;
    }

    public void Start(CancellationToken cancellationToken)
    {
        if (_lifetimeCts != null)
        {
            return;
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        unsafe
        {
            fixed (int* hotplugHandle = &_hotplugHandle)
            {
                _ = HidHotplugRegisterCallback(
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

        TrackBackgroundTask(RediscoverDevicesAsync("startup"), "startup rediscover");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hotplugHandle != 0)
        {
            HidHotplugDeregisterCallback(_hotplugHandle);
            _hotplugHandle = 0;
        }

        CancellationTokenSource? lifetime = _lifetimeCts;
        _lifetimeCts = null;
        lifetime?.Cancel();
        _offlineGate.CancelAll();
        await _offlineGate.WaitForPendingAsync(cancellationToken);

        Task[] background;
        lock (_backgroundSync)
        {
            background = _backgroundTasks.ToArray();
        }

        if (background.Length > 0)
        {
            try
            {
                await Task.WhenAll(background).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                NativeDiagnosticsStore.RecordError($"Manager background shutdown failed: {ex.GetBaseException().Message}");
            }
        }

        List<HidppDevices> sessions;
        lock (_sync)
        {
            sessions = [.. _sessions];
            _sessions.Clear();
        }

        foreach (HidppDevices session in sessions)
        {
            await session.DisposeAsync();
        }

        lifetime?.Dispose();
    }

    private void ScheduleRediscover(int delayMs = 1000, string reason = "scheduled")
    {
        CancellationToken token = LifetimeToken;
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _rediscoverQueued, 1) == 1)
        {
            QueueRediscoverAfterCurrent($"already queued after {reason}");
            return;
        }

        NativeDiagnosticsStore.AddEvent($"Rediscover scheduled in {delayMs}ms after {reason}");
        Task task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
                await RediscoverDevicesAsync(reason);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _rediscoverQueued, 0);
            }
        }, CancellationToken.None);
        TrackBackgroundTask(task, $"scheduled rediscover ({reason})");
    }

    private void ScheduleHotplugArrivalRediscover()
    {
        CancellationToken token = LifetimeToken;
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _hotplugArrivalRediscoverQueued, 1) == 1)
        {
            QueueRediscoverAfterCurrent("hotplug arrival burst already queued");
            return;
        }

        NativeDiagnosticsStore.AddEvent("Hotplug arrival detected; scheduling fast rediscover burst");
        Task task = Task.Run(async () =>
        {
            int previousDelayMs = 0;
            try
            {
                foreach (int delayMs in HotplugArrivalRediscoverDelaysMs)
                {
                    int waitMs = Math.Max(0, delayMs - previousDelayMs);
                    previousDelayMs = delayMs;
                    await Task.Delay(waitMs, token);
                    await RediscoverDevicesAsync("hotplugArrival");
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _hotplugArrivalRediscoverQueued, 0);
            }
        }, CancellationToken.None);
        TrackBackgroundTask(task, "hotplug arrival rediscover");
    }

    private void QueueRediscoverAfterCurrent(string reason)
    {
        if (LifetimeToken.IsCancellationRequested)
        {
            return;
        }

        Interlocked.Exchange(ref _rediscoverRequestedWhileRunning, 1);
        NativeDiagnosticsStore.AddEvent($"Rediscover queued after active discovery ({reason})");
    }

    private void ScheduleQueuedRediscoverIfNeeded()
    {
        if (LifetimeToken.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _rediscoverRequestedWhileRunning, 0) == 1)
        {
            ScheduleRediscover(REDISCOVER_FOLLOW_UP_DELAY_MS, "queuedDuringDiscovery");
        }
    }

    public void RediscoverDevices()
    {
        TrackBackgroundTask(RediscoverDevicesAsync("requested"), "requested rediscover");
    }

    public async Task RediscoverDevicesAsync(string reason)
    {
        CancellationToken token = LifetimeToken;
        if (token.IsCancellationRequested)
        {
            return;
        }

        bool manualRequest = reason.Equals("manualRequest", StringComparison.OrdinalIgnoreCase);
        if (manualRequest)
        {
            await _rediscoverLock.WaitAsync(token);
        }
        else if (!await _rediscoverLock.WaitAsync(0, token))
        {
            NativeDiagnosticsStore.AddEvent($"Rediscover skipped; discovery already running after {reason}");
            QueueRediscoverAfterCurrent(reason);
            return;
        }

        List<HidppDevices> createdForAttempt = [];
        try
        {
            IReadOnlyCollection<HidEndpointInfo> endpoints = EnumerateEndpoints();
            NativeDiagnosticsStore.BeginDiscovery(endpoints);
            List<SessionDescriptor> descriptors = CreateSessionDescriptors(endpoints);

            List<HidppDevices> current;
            lock (_sync)
            {
                current = [.. _sessions];
            }

            Dictionary<string, HidppDevices> existing = current
                .Where(session => !session.Disposed)
                .GroupBy(session => session.SessionConfigurationKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            List<HidppDevices> next = [];
            foreach (SessionDescriptor descriptor in descriptors)
            {
                if (existing.Remove(descriptor.Key, out HidppDevices? reusable))
                {
                    next.Add(reusable);
                }
                else
                {
                    HidppDevices session = new(descriptor.ShortEndpoint, descriptor.LongEndpoint);
                    next.Add(session);
                    createdForAttempt.Add(session);
                }
            }

            List<HidppDevices> removed = existing.Values.ToList();
            lock (_sync)
            {
                _sessions.Clear();
                _sessions.AddRange(next);
            }

            foreach (HidppDevices session in removed)
            {
                session.SignalKnownDevicesOffline("endpointRemoved");
                await session.DisposeAsync();
                if (session.ReaderShutdownTimedOut)
                {
                    throw new InvalidOperationException(
                        $"HID reader shutdown timed out for endpoint {session.EndpointIdentityKey}; replacement was aborted."
                    );
                }
            }

            bool forcePresenceReport = manualRequest ||
                                       reason.Equals("requested", StringComparison.OrdinalIgnoreCase);
            foreach (HidppDevices session in next)
            {
                token.ThrowIfCancellationRequested();
                if (createdForAttempt.Contains(session))
                {
                    await session.StartAsync();
                    if (session.Disposed)
                    {
                        throw new InvalidOperationException(
                            $"HID session failed to start for endpoint {session.EndpointIdentityKey}."
                        );
                    }
                }
                else
                {
                    await session.RefreshDiscoveryAsync(forcePresenceReport);
                    if (session.Disposed)
                    {
                        throw new InvalidOperationException(
                            $"HID session became unavailable while refreshing endpoint {session.EndpointIdentityKey}."
                        );
                    }
                }
            }

            NativeDiagnosticsStore.AddEvent(
                $"Rediscover completed after {reason}; reused={next.Count - createdForAttempt.Count}; created={createdForAttempt.Count}; removed={removed.Count}"
            );
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _sessions.RemoveAll(session => createdForAttempt.Contains(session));
            }

            foreach (HidppDevices session in createdForAttempt)
            {
                await session.DisposeAsync();
            }

            NativeDiagnosticsStore.RecordError($"Rediscover failed after {reason}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            _rediscoverLock.Release();
            ScheduleQueuedRediscoverIfNeeded();
        }
    }

    public async Task ProbePresenceAsync(CancellationToken cancellationToken)
    {
        await _rediscoverLock.WaitAsync(cancellationToken);
        try
        {
            List<HidppDevices> snapshot;
            lock (_sync)
            {
                snapshot = [.. _sessions];
            }

            if (snapshot.Count == 0)
            {
                ScheduleRediscover(0, "healthCheckWithoutSessions");
                return;
            }

            await Task.WhenAll(snapshot.Select(session => session.ProbePresenceAsync())).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            NativeDiagnosticsStore.RecordError($"Presence probe failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _rediscoverLock.Release();
        }
    }

    public async Task ForceBatteryUpdates()
    {
        CancellationToken cancellationToken = LifetimeToken;
        await _rediscoverLock.WaitAsync(cancellationToken);
        try
        {
            List<HidppDevices> snapshot;
            lock (_sync)
            {
                snapshot = [.. _sessions];
            }

            Task[] tasks = snapshot
                .SelectMany(session => session.DeviceCollection.Values)
                .Select(device => device.UpdateBattery(true))
                .ToArray();

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            NativeDiagnosticsStore.RecordError($"Forced battery update failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _rediscoverLock.Release();
        }
    }

    private void TrackBackgroundTask(Task task, string context)
    {
        lock (_backgroundSync)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(completed =>
        {
            lock (_backgroundSync)
            {
                _backgroundTasks.Remove(completed);
            }

            if (completed.IsFaulted && completed.Exception != null)
            {
                NativeDiagnosticsStore.RecordError($"{context} failed: {completed.Exception.GetBaseException().Message}");
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static List<SessionDescriptor> CreateSessionDescriptors(IReadOnlyCollection<HidEndpointInfo> endpoints)
    {
        List<SessionDescriptor> sessions = [];

        foreach (IGrouping<string, HidEndpointInfo> group in endpoints.GroupBy(x => x.GroupKey))
        {
            List<HidEndpointInfo> logitechEndpoints = group
                .Where(x => x.VendorId == LOGITECH_VENDOR_ID && x.OpenStatus.Equals("opened", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (logitechEndpoints.Count == 0)
            {
                continue;
            }

            List<HidEndpointInfo> centurions = logitechEndpoints
                .Where(x => x.MessageType == HidppMessageType.CENTURION && KnownLogitechDevices.IsCenturionProduct(x.ProductId))
                .OrderBy(x => x.UsagePage)
                .ThenBy(x => x.Path)
                .ToList();

            if (centurions.Count > 0)
            {
                sessions.Add(CreateDescriptor(centurions[0], null));
                continue;
            }

            List<HidEndpointInfo> shorts = logitechEndpoints
                .Where(x => x.MessageType == HidppMessageType.SHORT)
                .OrderBy(x => x.UsagePage)
                .ThenBy(x => x.Path)
                .ToList();

            List<HidEndpointInfo> longs = logitechEndpoints
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
                sessions.Add(CreateDescriptor(shorts[0], longs[0]));
                continue;
            }

            foreach (HidEndpointInfo shortEndpoint in shorts)
            {
                sessions.Add(CreateDescriptor(shortEndpoint, null));
            }
        }

        return sessions;
    }

    private static SessionDescriptor CreateDescriptor(HidEndpointInfo shortEndpoint, HidEndpointInfo? longEndpoint)
    {
        string key = $"{shortEndpoint.SafeId}:{shortEndpoint.PathHash}|{longEndpoint?.SafeId}:{longEndpoint?.PathHash}";
        return new SessionDescriptor(key, shortEndpoint, longEndpoint);
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
                if (deviceInfo.VendorId != LOGITECH_VENDOR_ID)
                {
                    continue;
                }

                HidppMessageType messageType = deviceInfo.GetHidppMessageType();
                string path = deviceInfo.GetPath();
                string? serialNumber = deviceInfo.GetSerialNumber();
                string serialNumberHash = HidppDeviceIdentity.IsMeaningfulTextIdentifier(serialNumber)
                    ? NativeDiagnosticsStore.HashForDiagnostics(serialNumber)
                    : string.Empty;
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
                        serialNumberHash,
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
                        serialNumberHash,
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

    private sealed record SessionDescriptor(
        string Key,
        HidEndpointInfo ShortEndpoint,
        HidEndpointInfo? LongEndpoint
    );
}
