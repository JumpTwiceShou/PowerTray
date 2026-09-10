using LGSTrayHID.HidApi;
using LGSTrayHID.Features;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID
{
    public sealed class HidppDevices : IDisposable, IAsyncDisposable
    {
        public const byte SW_ID = 0x0A;

        private const int READ_TIMEOUT = 100;
        private const int DEFAULT_COMMAND_TIMEOUT = 250;
        private const int COMMAND_QUEUE_TIMEOUT = 5000;
        private const int THREAD_JOIN_TIMEOUT_MS = 2000;
        private const int C54D_COMMAND_TIMEOUT = 600;
        private const int COMMAND_RETRY_DELAY_MS = 40;
        private const int ENDPOINT_READY_DELAY_MS = 120;
        private const int SPECULATIVE_PROBE_TIMEOUT_MS = 150;
        private const int RECEIVER_DISCOVERY_TIMEOUT_MS = 300;
        private const int RECEIVER_SETTLE_DELAY_MS = 120;
        private const int RECEIVER_FALLBACK_SETTLE_DELAY_MS = 50;
        private const ushort LIGHTSPEED_C54D_RECEIVER = 0xC54D;
        private const byte DEVICE_CONNECTION_NOTIFICATION = 0x41;
        private const byte DEVICE_DISCONNECTED_FLAG = 0x40;
        private const byte CENTURION_REPORT_ID = CenturionFrameCodec.ReportId;
        private const byte CENTURION_ADDRESSED_REPORT_ID = CenturionFrameCodec.AddressedReportId;
        private delegate Task<byte[]?> CenturionFeatureRequest(
            byte featureIndex,
            byte function,
            byte[] parameters,
            HidppDevices self
        );
        private delegate Task<byte[]?> CenturionDeviceRequest(
            byte featureIndex,
            byte function,
            byte[] parameters
        );
        private sealed record CenturionPresenceTarget(
            string DeviceId,
            IReadOnlyDictionary<ushort, byte> Features,
            CenturionDeviceRequest Request
        );
        private sealed record CenturionFeatureDiscovery(
            IReadOnlyDictionary<ushort, byte> Features,
            IReadOnlyList<CenturionFeatureDescriptor> Descriptors
        );
        private static readonly TimeSpan[] UnknownDeviceInitializationDelays =
        [
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMilliseconds(2500),
            TimeSpan.FromMilliseconds(5000),
        ];
        private static readonly TimeSpan[] CenturionOnlineRecoveryDelays =
        [
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(3000),
        ];

        private readonly HidEndpointInfo _shortEndpoint;
        private readonly HidEndpointInfo? _longEndpoint;
        private readonly DiscoverySessionDiagnostic _diagnostics;
        private readonly Dictionary<ushort, HidppDevice> _deviceCollection = [];
        private readonly Dictionary<byte, Task> _deviceInitializationTasks = [];
        private readonly Dictionary<byte, CancellationTokenSource> _deviceInitializationRetryCts = [];
        private readonly object _handleSync = new();
        private readonly object _reopenSync = new();
        private readonly object _taskSync = new();
        private readonly object _centurionPresenceSync = new();
        private readonly HashSet<SafeHidDeviceHandle> _expectedClosedHandles = [];
        private readonly Dictionary<SafeHidDeviceHandle, Thread> _readerThreads = [];
        private readonly List<Task> _backgroundTasks = [];
        private readonly HashSet<string> _knownDeviceIds = [];
        private readonly SemaphoreSlim _commandSemaphore = new(1, 1);
        private readonly SemaphoreSlim _centurionCommandSemaphore = new(1, 1);
        private readonly SemaphoreSlim _centurionDiscoverySemaphore = new(1, 1);
        private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        private HidDevicePtr _devShort = IntPtr.Zero;
        private HidDevicePtr _devLong = IntPtr.Zero;
        private CancellationTokenSource? _readCts;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private byte _pingPayload = 0x55;
        private byte _centurionSwId = 0x01;
        private byte _centurionReportId = CENTURION_REPORT_ID;
        private bool _centurionReportIdConfirmed;
        private byte? _centurionDeviceAddress;
        private byte? _centurionBridgeIndex;
        private string? _centurionDeviceId;
        private CenturionConnectionState? _centurionConnectionState;
        private long _centurionConnectionGeneration;
        private int _centurionProbeAttempts;
        private int _centurionInitializationPending;
        private int _centurionDeferredInitScheduled;
        private readonly HashSet<string> _offlineSignalledDeviceIds = [];
        private readonly Dictionary<string, int> _centurionFailureCounts = [];
        private readonly HashSet<string> _centurionDeviceInfoLoads = [];
        private int _disposeCount;
        private int _started;
        private int _readerShutdownTimedOut;
        private int _receiverDetected;
        private CenturionPresenceTarget? _centurionPresenceTarget;
        private Task? _disposeTask;

        public IReadOnlyDictionary<ushort, HidppDevice> DeviceCollection => _deviceCollection;
        internal bool HasKnownDevices
        {
            get
            {
                lock (_knownDeviceIds)
                {
                    return _knownDeviceIds.Count > 0;
                }
            }
        }
        internal bool HasDirectDevice
        {
            get
            {
                lock (_deviceCollection)
                {
                    return HidSessionRecoveryPolicy.ShouldBypassOfflineDeferral(
                        Volatile.Read(ref _receiverDetected) != 0,
                        _deviceCollection.Keys
                    );
                }
            }
        }
        internal string[] KnownDeviceIds
        {
            get
            {
                lock (_knownDeviceIds)
                {
                    return [.. _knownDeviceIds];
                }
            }
        }
        public HidDevicePtr DevShort => _devShort;
        public HidDevicePtr DevLong => _devLong;
        public ushort ProductId => _shortEndpoint.ProductId;
        public int InterfaceNumber => _shortEndpoint.InterfaceNumber;
        internal string EndpointIdentityKey => $"{_shortEndpoint.SafeId}:{_shortEndpoint.PathHash}";
        internal string ReceiverStableId => _shortEndpoint.ReceiverStableId;
        internal string PersistentEndpointAlias => _shortEndpoint.PersistentEndpointAlias;
        internal string SessionConfigurationKey => $"{EndpointIdentityKey}|{_longEndpoint?.SafeId}:{_longEndpoint?.PathHash}";
        internal byte CenturionReportId => _centurionReportId;
        internal byte? CenturionDeviceAddress => _centurionDeviceAddress;
        internal int CenturionProbeAttempts => _centurionProbeAttempts;
        internal bool MatchesEndpointPathHash(string pathHash) =>
            _shortEndpoint.PathHash.Equals(pathHash, StringComparison.OrdinalIgnoreCase)
            || (_longEndpoint?.PathHash.Equals(pathHash, StringComparison.OrdinalIgnoreCase) ?? false);
        public bool Disposed => _disposeCount > 0;
        internal bool ReaderShutdownTimedOut => Volatile.Read(ref _readerShutdownTimedOut) != 0;
        internal CancellationToken LifetimeToken => _lifetimeCts.Token;

        internal HidppDevices(HidEndpointInfo shortEndpoint, HidEndpointInfo? longEndpoint)
        {
            _shortEndpoint = shortEndpoint;
            _longEndpoint = longEndpoint;
            _centurionReportIdConfirmed = KnownLogitechDevices.TryGetCenturionReportId(
                shortEndpoint.ProductId,
                out _centurionReportId
            );
            if (!_centurionReportIdConfirmed)
            {
                _centurionReportId = CENTURION_REPORT_ID;
            }
            _diagnostics = NativeDiagnosticsStore.AddSession(shortEndpoint, longEndpoint);
        }

        public async Task StartAsync()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            if (Disposed)
            {
                return;
            }

            _readCts = new();
            _devShort = OpenEndpoint(_shortEndpoint);
            if (_devShort == IntPtr.Zero)
            {
                AddFailure("openFailed");
                Dispose();
                return;
            }

            StartReadThread(_devShort, _readCts.Token);

            if (_longEndpoint != null)
            {
                _devLong = OpenEndpoint(_longEndpoint);
                if (_devLong != IntPtr.Zero)
                {
                    StartReadThread(_devLong, _readCts.Token);
                }
                else
                {
                    AddFailure("openLongFailed");
                }
            }

            await DiscoverDevicesAsync();
        }

        internal async Task RefreshDiscoveryAsync(bool forcePresenceReport)
        {
            if (Disposed)
            {
                return;
            }

            NativeDiagnosticsStore.ReattachSession(_diagnostics);
            await DiscoverDevicesAsync();
            int attempts = forcePresenceReport ? GlobalSettings.settings.ConsecutiveFailureThreshold : 1;
            await ProbePresenceAsync(attempts, forcePresenceReport);
        }

        internal void ReattachDiagnostics()
        {
            NativeDiagnosticsStore.ReattachSession(_diagnostics);
        }

        internal void RecoverTransport(string reason)
        {
            lock (_reopenSync)
            {
                if (Disposed)
                {
                    return;
                }

                ReopenShortEndpointCore(reason);
                if (_longEndpoint != null)
                {
                    ReopenLongEndpointCore(reason);
                }
            }
        }

        internal async Task ProbePresenceAsync(int attempts = 1, bool forcePublish = false)
        {
            HidppDevice[] devices;
            lock (_deviceCollection)
            {
                devices = _deviceCollection.Values.ToArray();
            }

            foreach (HidppDevice device in devices)
            {
                for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
                {
                    if (await device.ProbePresenceAsync(forcePublish))
                    {
                        break;
                    }

                    if (attempt < attempts)
                    {
                        await Task.Delay(COMMAND_RETRY_DELAY_MS * attempt, _lifetimeCts.Token);
                    }
                }
            }

            CenturionPresenceTarget? centurionTarget;
            lock (_centurionPresenceSync)
            {
                centurionTarget = _centurionPresenceTarget;
            }

            if (centurionTarget == null)
            {
                return;
            }

            for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
            {
                if (await ProbeCenturionPresenceAsync(centurionTarget, forcePublish))
                {
                    break;
                }

                if (attempt < attempts)
                {
                    await Task.Delay(COMMAND_RETRY_DELAY_MS * attempt, _lifetimeCts.Token);
                }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public ValueTask DisposeAsync()
        {
            lock (_taskSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            if (Interlocked.Exchange(ref _disposeCount, 1) != 0)
            {
                return;
            }

            _readCts?.Cancel();
            _lifetimeCts.Cancel();
            _channel.Writer.TryComplete();
            CancellationTokenSource[] initializationRetries;
            lock (_deviceCollection)
            {
                initializationRetries = [.. _deviceInitializationRetryCts.Values];
                _deviceInitializationRetryCts.Clear();
            }
            foreach (CancellationTokenSource retryCts in initializationRetries)
            {
                retryCts.Cancel();
            }

            HidDevicePtr shortHandle;
            HidDevicePtr longHandle;
            lock (_reopenSync)
            {
                lock (_handleSync)
                {
                    shortHandle = _devShort;
                    longHandle = _devLong;
                    MarkExpectedClose(shortHandle);
                    MarkExpectedClose(longHandle);
                    _devShort = IntPtr.Zero;
                    _devLong = IntPtr.Zero;
                }

                CloseEndpoint(shortHandle);
                CloseEndpoint(longHandle);
            }

            Thread[] readers;
            lock (_taskSync)
            {
                readers = _readerThreads.Values.Distinct().ToArray();
            }

            await Task.Run(() =>
            {
                foreach (Thread reader in readers)
                {
                    if (reader.IsAlive && !reader.Join(THREAD_JOIN_TIMEOUT_MS))
                    {
                        Interlocked.Exchange(ref _readerShutdownTimedOut, 1);
                        NativeDiagnosticsStore.RecordError($"HID read thread did not stop within {THREAD_JOIN_TIMEOUT_MS}ms.");
                    }
                }
            });

            while (true)
            {
                Task[] backgroundTasks;
                lock (_taskSync)
                {
                    backgroundTasks = _backgroundTasks.ToArray();
                }

                if (backgroundTasks.Length == 0)
                {
                    break;
                }

                try
                {
                    await Task.WhenAll(backgroundTasks);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    NativeDiagnosticsStore.RecordError($"HID background task shutdown failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _readCts?.Dispose();
            _readCts = null;
            _lifetimeCts.Dispose();
            _commandSemaphore.Dispose();
            _centurionCommandSemaphore.Dispose();
            _centurionDiscoverySemaphore.Dispose();
        }

        internal void TrackBackgroundTask(Task task)
        {
            lock (_taskSync)
            {
                _backgroundTasks.Add(task);
            }

            _ = task.ContinueWith(completed =>
            {
                lock (_taskSync)
                {
                    _backgroundTasks.Remove(completed);
                }

                if (completed.IsFaulted && completed.Exception != null)
                {
                    NativeDiagnosticsStore.RecordError($"HID background task faulted: {completed.Exception.GetBaseException().Message}");
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static HidDevicePtr OpenEndpoint(HidEndpointInfo endpoint)
        {
            nint dev = HidOpenPath(endpoint.Path);
#if DEBUG
            if (dev == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to open {endpoint.Path}");
            }
            else
            {
                Console.WriteLine($"Opened {endpoint.MessageType} {endpoint.ProductId:X4} {endpoint.UsagePage:X4}:{endpoint.Usage:X4} {endpoint.Path}");
            }
#endif
            return dev;
        }

        private void StartReadThread(HidDevicePtr dev, CancellationToken cancellationToken)
        {
            if (dev.SafeHandle == null)
            {
                return;
            }

            Thread thread = new(() => ReadLoop(dev, cancellationToken))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"PowerTray HID reader {EndpointIdentityKey}",
            };
            lock (_taskSync)
            {
                _readerThreads[dev.SafeHandle] = thread;
            }
            thread.Start();
        }

        private void ReadLoop(HidDevicePtr dev, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[64];
            bool readFailed = false;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Array.Clear(buffer);
                    int read = dev.Read(buffer, buffer.Length, READ_TIMEOUT);
                    if (read < 0)
                    {
                        readFailed = true;
                        break;
                    }

                    if (read == 0)
                    {
                        continue;
                    }

                    ProcessMessage(buffer[..Math.Min(read, buffer.Length)]);
                }
            }
            finally
            {
                if (readFailed && !cancellationToken.IsCancellationRequested && !ConsumeExpectedClose(dev))
                {
                    SignalKnownDevicesOffline("readFailed");
                }

                CloseEndpoint(dev);
                if (dev.SafeHandle != null)
                {
                    lock (_taskSync)
                    {
                        _readerThreads.Remove(dev.SafeHandle);
                    }
                }
            }
        }

        private void ProcessMessage(byte[] buffer)
        {
            if (_shortEndpoint.MessageType == HidppMessageType.CENTURION)
            {
                CenturionTraceWriter.Record(
                    "rx",
                    _shortEndpoint.ProductId,
                    _shortEndpoint.PathHash,
                    buffer
                );

                ObserveCenturionFrame(buffer);
                if (TryHandleCenturionConnectionNotification(buffer))
                {
                    return;
                }

                if (TryHandleCenturionBridgeNotification(buffer))
                {
                    return;
                }
            }

            if (buffer.Length < 4)
            {
                return;
            }

            if (buffer[0] == 0x10 && buffer.Length >= 7 && buffer[2] == DEVICE_CONNECTION_NOTIFICATION)
            {
                if ((buffer[4] & DEVICE_DISCONNECTED_FLAG) == 0)
                {
                    SignalOnline(buffer[1]);
                }
                else
                {
                    SignalOffline(buffer[1]);
                }

                return;
            }

            if (TryHandleStandardBatteryNotification(buffer))
            {
                return;
            }

            if (!_channel.Writer.TryWrite(buffer) && !Disposed)
            {
                NativeDiagnosticsStore.RecordError("HID response channel rejected a response.");
            }
        }

        private bool TryHandleStandardBatteryNotification(byte[] buffer)
        {
            if (buffer.Length < 7 ||
                (buffer[0] != 0x10 && buffer[0] != 0x11) ||
                buffer[3] != 0x00)
            {
                return false;
            }

            HidppDevice? device;
            lock (_deviceCollection)
            {
                _deviceCollection.TryGetValue(buffer[1], out device);
            }

            return device?.TryHandleBatteryNotification(buffer) == true;
        }

        private void ObserveCenturionFrame(byte[] buffer)
        {
            if (!CenturionFrameCodec.TryExtractPayload(
                    buffer,
                    out byte reportId,
                    out byte? deviceAddress,
                    out _
                ))
            {
                return;
            }

            bool learnedTransport = false;
            bool scheduleDeferredInitialization = false;
            lock (_centurionPresenceSync)
            {
                if (!_centurionReportIdConfirmed)
                {
                    _centurionReportId = reportId;
                    _centurionReportIdConfirmed = true;
                    learnedTransport = true;
                }
                else if (_centurionReportId != reportId)
                {
                    return;
                }

                if (reportId == CENTURION_ADDRESSED_REPORT_ID)
                {
                    if (_centurionDeviceAddress == null)
                    {
                        _centurionDeviceAddress = deviceAddress;
                        learnedTransport = true;
                    }
                    else if (_centurionDeviceAddress != deviceAddress)
                    {
                        return;
                    }
                }

                scheduleDeferredInitialization = Volatile.Read(ref _centurionInitializationPending) != 0;
            }

            if (learnedTransport)
            {
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
                    x.Centurion.DeviceAddress = _centurionDeviceAddress.HasValue
                        ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2)
                        : null;
                });
                NativeDiagnosticsStore.AddEvent(
                    $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: learned Centurion transport from first RX frame report={NativeDiagnosticsStore.FormatHex(_centurionReportId, 2)} address={(_centurionDeviceAddress.HasValue ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2) : "none")}"
                );
            }

            if (scheduleDeferredInitialization)
            {
                ScheduleDeferredCenturionInitialization();
            }
        }

        private void ScheduleDeferredCenturionInitialization()
        {
            if (Interlocked.CompareExchange(ref _centurionDeferredInitScheduled, 1, 0) != 0 ||
                Disposed)
            {
                return;
            }

            Task initializationTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, _lifetimeCts.Token);
                    _ = await TryDiscoverCenturionAsync();
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _centurionDeferredInitScheduled, 0);
                }
            }, _lifetimeCts.Token);
            TrackBackgroundTask(initializationTask);
        }

        private bool TryHandleCenturionBridgeNotification(byte[] buffer)
        {
            byte? bridgeIndex;
            CenturionPresenceTarget? target;
            lock (_centurionPresenceSync)
            {
                bridgeIndex = _centurionBridgeIndex;
                target = _centurionPresenceTarget;
            }

            if (!bridgeIndex.HasValue ||
                !CenturionBridgeNotificationCodec.TryDecode(
                    buffer,
                    _centurionReportId,
                    _centurionDeviceAddress,
                    bridgeIndex.Value,
                    out CenturionBridgeNotification? notification
                ) ||
                notification == null)
            {
                return false;
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: Centurion bridge event feature={NativeDiagnosticsStore.FormatHex(notification.FeatureIndex, 2)} function={NativeDiagnosticsStore.FormatHex(notification.Function, 2)}"
            );

            if (target == null)
            {
                lock (_centurionPresenceSync)
                {
                    if (_centurionConnectionState != CenturionConnectionState.Connected)
                    {
                        _centurionConnectionState = CenturionConnectionState.Connected;
                        _centurionConnectionGeneration++;
                    }
                }
                Interlocked.Exchange(ref _centurionInitializationPending, 1);
                ScheduleDeferredCenturionInitialization();
                return true;
            }

            ushort? featureId = null;
            foreach (KeyValuePair<ushort, byte> feature in target.Features)
            {
                if (feature.Value == notification.FeatureIndex)
                {
                    featureId = feature.Key;
                    break;
                }
            }

            CenturionConnectionState? previousState;
            long generation;
            lock (_centurionPresenceSync)
            {
                previousState = _centurionConnectionState;
                _centurionConnectionState = CenturionConnectionState.Connected;
                generation = previousState == CenturionConnectionState.Connected
                    ? _centurionConnectionGeneration
                    : ++_centurionConnectionGeneration;
            }

            if (featureId == 0x0104)
            {
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.BatteryRawResponse = NativeDiagnosticsStore.FormatBytes(notification.Data);
                });
                UpdateMessage? update = CreateCenturionBatteryUpdate(target.DeviceId, notification.Data);
                if (update != null)
                {
                    ResetCenturionTransportFailures(target.DeviceId);
                    HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, update);
                    return true;
                }
            }

            if (previousState != CenturionConnectionState.Connected)
            {
                Task recoveryTask = RecoverCenturionOnlineAsync(generation);
                TrackBackgroundTask(recoveryTask);
            }
            return true;
        }

        private bool TryHandleCenturionConnectionNotification(byte[] buffer)
        {
            CenturionConnectionState state;
            CenturionConnectionState? previousState;
            long generation;
            string? deviceId;
            lock (_centurionPresenceSync)
            {
                if (!_centurionBridgeIndex.HasValue ||
                    !CenturionConnectionNotificationCodec.TryDecode(
                        buffer,
                        _centurionReportId,
                        _centurionDeviceAddress,
                        _centurionBridgeIndex.Value,
                        out state
                    ))
                {
                    return false;
                }

                previousState = _centurionConnectionState;
                _centurionConnectionState = state;
                generation = previousState == state
                    ? _centurionConnectionGeneration
                    : ++_centurionConnectionGeneration;
                deviceId = _centurionDeviceId ?? _centurionPresenceTarget?.DeviceId;
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: Centurion connection notification state={state.ToString().ToLowerInvariant()} bridge={NativeDiagnosticsStore.FormatHex(_centurionBridgeIndex!.Value, 2)}"
            );

            if (state == CenturionConnectionState.Disconnected)
            {
                if (previousState != CenturionConnectionState.Disconnected &&
                    !string.IsNullOrWhiteSpace(deviceId))
                {
                    SignalOffline(deviceId, bypassDeferral: true);
                }
                return true;
            }

            if (previousState != CenturionConnectionState.Connected)
            {
                Task recoveryTask = RecoverCenturionOnlineAsync(generation);
                TrackBackgroundTask(recoveryTask);
            }
            return true;
        }

        private async Task RecoverCenturionOnlineAsync(long generation)
        {
            foreach (TimeSpan delay in CenturionOnlineRecoveryDelays)
            {
                try
                {
                    await Task.Delay(delay, _lifetimeCts.Token);
                    if (!IsCurrentCenturionConnection(generation, CenturionConnectionState.Connected))
                    {
                        return;
                    }

                    CenturionPresenceTarget? target;
                    lock (_centurionPresenceSync)
                    {
                        target = _centurionPresenceTarget;
                    }

                    if (target == null)
                    {
                        _ = await TryDiscoverCenturionAsync();
                        if (!IsCurrentCenturionConnection(generation, CenturionConnectionState.Connected))
                        {
                            return;
                        }

                        lock (_centurionPresenceSync)
                        {
                            target = _centurionPresenceTarget;
                        }
                    }

                    if (target == null)
                    {
                        continue;
                    }

                    UpdateMessage? update = await CreateCenturionBatteryUpdateAsync(
                        target.DeviceId,
                        target.Features,
                        target.Request
                    );
                    if (update == null ||
                        !IsCurrentCenturionConnection(generation, CenturionConnectionState.Connected))
                    {
                        continue;
                    }

                    ResetCenturionTransportFailures(target.DeviceId);
                    HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, update);
                    NativeDiagnosticsStore.AddEvent(
                        $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: Centurion online confirmed by fresh battery response"
                    );
                    return;
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    NativeDiagnosticsStore.RecordError(
                        $"Centurion online recovery failed: {ex.GetType().Name}: {ex.Message}"
                    );
                }
            }

            if (IsCurrentCenturionConnection(generation, CenturionConnectionState.Connected))
            {
                NativeDiagnosticsStore.AddEvent(
                    $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: Centurion online notification was not confirmed; presence probing remains active"
                );
            }
        }

        private bool IsCurrentCenturionConnection(long generation, CenturionConnectionState state)
        {
            lock (_centurionPresenceSync)
            {
                return _centurionConnectionGeneration == generation &&
                    _centurionConnectionState == state;
            }
        }

        private Task QueueDeviceInitAsync(byte deviceIdx)
        {
            Task? initializationTask;
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            HidppDevice device;
            lock (_deviceCollection)
            {
                if (Disposed)
                {
                    return Task.CompletedTask;
                }

                if (_deviceInitializationTasks.TryGetValue(deviceIdx, out initializationTask))
                {
                    return initializationTask;
                }

                if (_deviceCollection.ContainsKey(deviceIdx))
                {
                    return Task.CompletedTask;
                }

                device = new(this, deviceIdx);
                _deviceCollection[deviceIdx] = device;
                initializationTask = InitializeDeviceAsync(ready.Task, deviceIdx, device);
                _deviceInitializationTasks[deviceIdx] = initializationTask;
            }

            TrackBackgroundTask(initializationTask);
            ready.SetResult();
            return initializationTask;
        }

        private void ScheduleUnknownDeviceInitialization(byte deviceIdx)
        {
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            CancellationTokenSource? previous = null;
            lock (_deviceCollection)
            {
                if (Disposed)
                {
                    cts.Dispose();
                    return;
                }

                if (_deviceInitializationRetryCts.Remove(deviceIdx, out CancellationTokenSource? existing))
                {
                    previous = existing;
                }
                _deviceInitializationRetryCts[deviceIdx] = cts;
            }

            previous?.Cancel();
            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: scheduling unknown online device initialization index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
            );

            Task retryTask = Task.Run(async () =>
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                try
                {
                    foreach (TimeSpan delay in UnknownDeviceInitializationDelays)
                    {
                        TimeSpan wait = delay - elapsed.Elapsed;
                        if (wait > TimeSpan.Zero)
                        {
                            await Task.Delay(wait, cts.Token);
                        }

                        await QueueDeviceInitAsync(deviceIdx);
                        if (IsDeviceInitialized(deviceIdx))
                        {
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                }
                finally
                {
                    lock (_deviceCollection)
                    {
                        if (_deviceInitializationRetryCts.TryGetValue(deviceIdx, out CancellationTokenSource? current) &&
                            ReferenceEquals(current, cts))
                        {
                            _deviceInitializationRetryCts.Remove(deviceIdx);
                        }
                    }
                    cts.Dispose();
                }
            }, CancellationToken.None);
            TrackBackgroundTask(retryTask);
        }

        private bool IsDeviceInitialized(byte deviceIdx)
        {
            lock (_deviceCollection)
            {
                return _deviceCollection.TryGetValue(deviceIdx, out HidppDevice? device) &&
                       !string.IsNullOrWhiteSpace(device.Identifier);
            }
        }

        private async Task InitializeDeviceAsync(Task ready, byte deviceIdx, HidppDevice device)
        {
            await ready;
            try
            {
                await device.InitAsync();
                if (string.IsNullOrWhiteSpace(device.Identifier))
                {
                    RemoveUninitializedDevice(deviceIdx, device, "missingIdentifier");
                }
                else if (Volatile.Read(ref _receiverDetected) != 0 &&
                         HidDeviceIndexCache.RememberReceiverSlot(_shortEndpoint, deviceIdx))
                {
                    NativeDiagnosticsStore.AddEvent(
                        $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: remembered protocol-confirmed receiver slot={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
                    );
                }
                else if (Volatile.Read(ref _receiverDetected) == 0 &&
                         HidDeviceIndexCache.RememberDirect(_shortEndpoint, deviceIdx))
                {
                    NativeDiagnosticsStore.AddEvent(
                        $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: remembered protocol-confirmed direct index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
                    );
                }
            }
            catch (Exception ex)
            {
                RemoveUninitializedDevice(deviceIdx, device, ex.GetType().Name);
#if DEBUG
                Console.WriteLine($"Failed to initialise device index {deviceIdx}: {ex}");
#else
                System.Diagnostics.Debug.WriteLine($"Failed to initialise device index {deviceIdx}: {ex}");
#endif
            }
            finally
            {
                lock (_deviceCollection)
                {
                    _deviceInitializationTasks.Remove(deviceIdx);
                }
            }
        }

        private void RemoveUninitializedDevice(byte deviceIdx, HidppDevice device, string reason)
        {
            lock (_deviceCollection)
            {
                if (_deviceCollection.TryGetValue(deviceIdx, out HidppDevice? current) && ReferenceEquals(current, device))
                {
                    _deviceCollection.Remove(deviceIdx);
                }
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: device initialization removed index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)} reason={reason}"
            );
        }

        private void SignalOnline(byte deviceIdx)
        {
            HidppDevice? device;
            lock (_deviceCollection)
            {
                _deviceCollection.TryGetValue(deviceIdx, out device);
            }

            if (device == null || string.IsNullOrWhiteSpace(device.Identifier))
            {
                ScheduleUnknownDeviceInitialization(deviceIdx);
                return;
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: device online notification index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
            );
            device.SignalOnline();
        }

        private void SignalOffline(byte deviceIdx)
        {
            HidppDevice? device;
            lock (_deviceCollection)
            {
                _deviceCollection.TryGetValue(deviceIdx, out device);
            }

            if (device == null || string.IsNullOrWhiteSpace(device.Identifier))
            {
                return;
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: device offline notification index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
            );
            device.SignalOffline();
        }

        private async Task DiscoverDevicesAsync()
        {
            if (_devShort == IntPtr.Zero)
            {
                AddFailure("openFailed");
                return;
            }

            bool recordTiming = !HasKnownDevices;
            Stopwatch elapsed = Stopwatch.StartNew();
            void RecordDiscoveryStage(string stage)
            {
                if (recordTiming)
                {
                    NativeDiagnosticsStore.AddEvent(
                        $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: discovery stage={stage} elapsedMs={elapsed.ElapsedMilliseconds}"
                    );
                }
            }

            await Task.Delay(ENDPOINT_READY_DELAY_MS);

            if (await TryDiscoverCenturionAsync())
            {
                RecordDiscoveryStage("centurionComplete");
                return;
            }

            if (HidDeviceIndexCache.TryGetDirect(_shortEndpoint, out byte cachedDeviceIdx))
            {
                RecordDiscoveryStage(
                    $"directCacheHit index={NativeDiagnosticsStore.FormatHex(cachedDeviceIdx, 2)}"
                );
                if (await Ping20(
                    cachedDeviceIdx,
                    SPECULATIVE_PROBE_TIMEOUT_MS,
                    false,
                    maxAttempts: 1
                ))
                {
                    await QueueDeviceInitAsync(cachedDeviceIdx);
                    if (IsDeviceInitialized(cachedDeviceIdx))
                    {
                        RecordDiscoveryStage(
                            $"directCacheReady index={NativeDiagnosticsStore.FormatHex(cachedDeviceIdx, 2)}"
                        );
                        return;
                    }
                }

                RecordDiscoveryStage(
                    $"directCacheFallback index={NativeDiagnosticsStore.FormatHex(cachedDeviceIdx, 2)}"
                );
            }

            bool cachedReceiverResponded = false;
            IReadOnlyList<byte> cachedReceiverSlots =
                HidDeviceIndexCache.GetReceiverSlots(_shortEndpoint);
            if (cachedReceiverSlots.Count > 0)
            {
                RecordDiscoveryStage(
                    $"receiverCacheHit slots={string.Join(",", cachedReceiverSlots.Select(index => NativeDiagnosticsStore.FormatHex(index, 2)))}"
                );
                foreach (byte cachedReceiverSlot in cachedReceiverSlots)
                {
                    if (Disposed)
                    {
                        return;
                    }

                    if (!await Ping20(
                        cachedReceiverSlot,
                        SPECULATIVE_PROBE_TIMEOUT_MS,
                        false,
                        maxAttempts: 1,
                        allowC54dRecovery: false
                    ))
                    {
                        RecordDiscoveryStage(
                            $"receiverCacheProbeMiss index={NativeDiagnosticsStore.FormatHex(cachedReceiverSlot, 2)}"
                        );
                        continue;
                    }

                    cachedReceiverResponded = true;
                    Interlocked.Exchange(ref _receiverDetected, 1);
                    RecordDiscoveryStage(
                        $"receiverCacheResponded index={NativeDiagnosticsStore.FormatHex(cachedReceiverSlot, 2)}"
                    );
                    await QueueDeviceInitAsync(cachedReceiverSlot);
                    if (IsDeviceInitialized(cachedReceiverSlot))
                    {
                        RecordDiscoveryStage(
                            $"receiverCacheReady index={NativeDiagnosticsStore.FormatHex(cachedReceiverSlot, 2)}"
                        );
                    }
                }
            }

            bool receiverResponded = await TryReceiverDiscoveryAsync();
            bool receiverTransport = receiverResponded || cachedReceiverResponded;
            if (receiverTransport)
            {
                Interlocked.Exchange(ref _receiverDetected, 1);
            }
            RecordDiscoveryStage(
                receiverResponded
                    ? "receiverDetected"
                    : cachedReceiverResponded
                        ? "receiverConfirmedFromCache"
                        : "receiverNotDetected"
            );
            if (receiverResponded)
            {
                await Task.Delay(RECEIVER_SETTLE_DELAY_MS);
            }
            else if (!cachedReceiverResponded)
            {
                await Task.Delay(RECEIVER_FALLBACK_SETTLE_DELAY_MS);
            }

            List<Task> initializationTasks = [];
            foreach (byte deviceIdx in GetProbeDeviceIndexes(receiverTransport))
            {
                if (Disposed)
                {
                    return;
                }

                Task? existingInitializationTask;
                bool deviceExists;
                lock (_deviceCollection)
                {
                    deviceExists = _deviceCollection.ContainsKey(deviceIdx);
                    _deviceInitializationTasks.TryGetValue(deviceIdx, out existingInitializationTask);
                }

                if (existingInitializationTask != null)
                {
                    initializationTasks.Add(existingInitializationTask);
                    continue;
                }

                if (deviceExists)
                {
                    continue;
                }

                if (await Ping20(
                    deviceIdx,
                    SPECULATIVE_PROBE_TIMEOUT_MS,
                    false,
                    maxAttempts: 1
                ))
                {
                    RecordDiscoveryStage(
                        $"probeResponded index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
                    );
                    Task initializationTask = QueueDeviceInitAsync(deviceIdx);
                    if (!receiverTransport && deviceIdx is 0x00 or 0xFF)
                    {
                        await initializationTask;
                        if (IsDeviceInitialized(deviceIdx))
                        {
                            RecordDiscoveryStage(
                                $"directReady index={NativeDiagnosticsStore.FormatHex(deviceIdx, 2)}"
                            );
                            return;
                        }
                    }
                    else
                    {
                        initializationTasks.Add(initializationTask);
                    }
                }
            }

            if (initializationTasks.Count > 0)
            {
                await Task.WhenAll(initializationTasks);
            }
            RecordDiscoveryStage("complete");
        }

        private static IEnumerable<byte> GetProbeDeviceIndexes(bool receiverResponded)
        {
            if (!receiverResponded)
            {
                yield return 0xFF;
                yield return 0x00;
            }

            for (byte i = 1; i <= 6; i++)
            {
                yield return i;
            }
        }

        private async Task<bool> TryReceiverDiscoveryAsync()
        {
            byte[] ret = await WriteRead10(
                _devShort,
                [0x10, 0xFF, 0x81, 0x02, 0x00, 0x00, 0x00],
                RECEIVER_DISCOVERY_TIMEOUT_MS
            );
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x => x.ReceiverDiscoveryResponse = NativeDiagnosticsStore.FormatBytes(ret));
            if (ret.Length < 6 || ret[2] != 0x81 || ret[3] != 0x02)
            {
                return false;
            }

            byte numDeviceFound = ret[5];
            if (numDeviceFound > 0)
            {
                _ = await WriteRead10(
                    _devShort,
                    [0x10, 0xFF, 0x80, 0x02, 0x02, 0x00, 0x00],
                    RECEIVER_DISCOVERY_TIMEOUT_MS
                );
            }

            return true;
        }

        private async Task<bool> TryDiscoverCenturionAsync()
        {
            if (_shortEndpoint.MessageType != HidppMessageType.CENTURION)
            {
                return false;
            }

            bool locked = await _centurionDiscoverySemaphore.WaitAsync(
                COMMAND_QUEUE_TIMEOUT,
                _lifetimeCts.Token
            );
            if (!locked)
            {
                AddFailure("centurionDiscoveryQueueTimeout");
                return true;
            }

            try
            {
                return await TryDiscoverCenturionCoreAsync();
            }
            finally
            {
                _centurionDiscoverySemaphore.Release();
            }
        }

        private async Task<bool> TryDiscoverCenturionCoreAsync()
        {
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
            });

            CenturionFeatureDiscovery dongleDiscovery = new(
                new Dictionary<ushort, byte>(),
                []
            );
            if (!_centurionReportIdConfirmed)
            {
                lock (_centurionPresenceSync)
                {
                    if (!_centurionReportIdConfirmed)
                    {
                        _centurionReportId = CENTURION_REPORT_ID;
                        _centurionDeviceAddress = null;
                    }
                }

                dongleDiscovery = await DiscoverCenturionFeaturesAsync(
                    static (featureIndex, function, parameters, self) =>
                        self.CenturionRequestAsync(featureIndex, function, parameters)
                );
                if (dongleDiscovery.Features.Count > 0)
                {
                    lock (_centurionPresenceSync)
                    {
                        _centurionReportIdConfirmed = true;
                    }
                }
                else
                {
                    lock (_centurionPresenceSync)
                    {
                        if (!_centurionReportIdConfirmed)
                        {
                            _centurionReportId = CENTURION_ADDRESSED_REPORT_ID;
                            _centurionDeviceAddress = null;
                        }
                    }
                }
            }

            if (dongleDiscovery.Features.Count == 0 &&
                _centurionReportId == CENTURION_ADDRESSED_REPORT_ID &&
                _centurionDeviceAddress == null)
            {
                _ = await ProbeCenturionDeviceAddressAsync();
                if (_centurionDeviceAddress == null)
                {
                    Interlocked.Exchange(ref _centurionInitializationPending, 1);
                    AddFailure("centurionAddressUnknown");
                    UpdateCenturionTransportDiagnostics();
                    return true;
                }

                lock (_centurionPresenceSync)
                {
                    _centurionReportIdConfirmed = true;
                }
            }

            if (dongleDiscovery.Features.Count == 0)
            {
                dongleDiscovery = await DiscoverCenturionFeaturesAsync(
                    static (featureIndex, function, parameters, self) =>
                        self.CenturionRequestAsync(featureIndex, function, parameters)
                );
            }

            IReadOnlyDictionary<ushort, byte> dongleFeatures = dongleDiscovery.Features;
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
                x.Centurion.DeviceAddress = _centurionDeviceAddress.HasValue ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2) : null;
                x.Centurion.ProbeAttempts = _centurionProbeAttempts;
                x.Centurion.DongleFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(dongleFeatures);
                x.Centurion.DongleFeatureMetadata = NativeDiagnosticsStore.FormatFeatureMetadata(
                    dongleDiscovery.Descriptors
                );
            });
            if (dongleFeatures.TryGetValue(0x0003, out byte bridgeIndex))
            {
                lock (_centurionPresenceSync)
                {
                    _centurionBridgeIndex = bridgeIndex;
                }
                CenturionFeatureDiscovery headsetDiscovery = await DiscoverCenturionFeaturesAsync(
                    (featureIndex, function, parameters, self) =>
                        self.CenturionBridgeRequestAsync(bridgeIndex, featureIndex, function, parameters)
                );
                IReadOnlyDictionary<ushort, byte> headsetFeatures = headsetDiscovery.Features;
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.BridgeIndex = NativeDiagnosticsStore.FormatHex(bridgeIndex, 2);
                    x.Centurion.SubDeviceFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(headsetFeatures);
                    x.Centurion.SubDeviceFeatureMetadata = NativeDiagnosticsStore.FormatFeatureMetadata(
                        headsetDiscovery.Descriptors
                    );
                });
                if (headsetFeatures.Count == 0)
                {
                    AddFailure("featureSetMissing");
                    return true;
                }

                Interlocked.Exchange(ref _centurionInitializationPending, 0);
                return await InitialiseCenturionDeviceAsync(
                    headsetFeatures,
                    (featureIndex, function, parameters) => CenturionBridgeRequestAsync(bridgeIndex, featureIndex, function, parameters),
                    GetCenturionFallbackName()
                );
            }

            if (dongleFeatures.Count > 0 && (_shortEndpoint.ProductId == 0x0B19 || dongleFeatures.ContainsKey(0x0104)))
            {
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.SubDeviceFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(dongleFeatures);
                    x.Centurion.SubDeviceFeatureMetadata = NativeDiagnosticsStore.FormatFeatureMetadata(
                        dongleDiscovery.Descriptors
                    );
                });
                Interlocked.Exchange(ref _centurionInitializationPending, 0);
                return await InitialiseCenturionDeviceAsync(
                    dongleFeatures,
                    (featureIndex, function, parameters) => CenturionRequestAsync(featureIndex, function, parameters),
                    GetCenturionFallbackName()
                );
            }

            if (dongleFeatures.Count == 0)
            {
                Interlocked.Exchange(ref _centurionInitializationPending, 1);
            }
            AddFailure("centurionBridgeMissing");
            return true;
        }

        private string GetCenturionFallbackName()
        {
            string? productString = _shortEndpoint.ProductString?.Trim();
            return HidppDeviceIdentity.IsMeaningfulTextIdentifier(productString)
                ? productString!
                : KnownLogitechDevices.GetFallbackName(DeviceType.Headset, _shortEndpoint.ProductId);
        }

        private void UpdateCenturionTransportDiagnostics()
        {
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
                x.Centurion.DeviceAddress = _centurionDeviceAddress.HasValue
                    ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2)
                    : null;
                x.Centurion.ProbeAttempts = _centurionProbeAttempts;
            });
        }

        private async Task<bool> InitialiseCenturionDeviceAsync(
            IReadOnlyDictionary<ushort, byte> features,
            CenturionDeviceRequest request,
            string fallbackName
        )
        {
            string name = await ReadCenturionNameAsync(features, request) ?? fallbackName;
            name = KnownLogitechDevices.GetDisplayName(name, DeviceType.Headset, _shortEndpoint.ProductId);
            string? serial = await ReadCenturionSerialAsync(features, request);
            if (!HidppDeviceIdentity.IsMeaningfulTextIdentifier(serial))
            {
                string identityAlias = string.Join('|',
                    "centurion",
                    string.IsNullOrWhiteSpace(ReceiverStableId) ? PersistentEndpointAlias : ReceiverStableId,
                    _shortEndpoint.ProductId.ToString("X4"),
                    _centurionReportId.ToString("X2"),
                    _centurionDeviceAddress?.ToString("X2") ?? string.Empty,
                    name
                );
                serial = PersistentDeviceIdentityStore.GetOrCreate(identityAlias);
            }
            string deviceId = $"centurion-{serial}";
            bool hasBattery = features.ContainsKey(0x0104);
            bool requireFreshResponse;
            lock (_centurionPresenceSync)
            {
                if (_centurionConnectionState == CenturionConnectionState.Disconnected)
                {
                    return true;
                }

                _centurionDeviceId = deviceId;
                _centurionPresenceTarget = hasBattery
                    ? new CenturionPresenceTarget(
                        deviceId,
                        new Dictionary<ushort, byte>(features),
                        request
                    )
                    : null;
                requireFreshResponse = _centurionConnectionState == CenturionConnectionState.Connected;
            }

            RecordDeviceDiscovery("0xFF", name, DeviceType.Headset, deviceId, features, "0x0104", null);

            UpdateMessage? initialBattery = null;
            if (hasBattery && requireFreshResponse)
            {
                initialBattery = await CreateCenturionBatteryUpdateAsync(deviceId, features, request);
                if (initialBattery == null)
                {
                    AddFailure("batteryReadFailed");
                    return true;
                }
            }

            lock (_centurionPresenceSync)
            {
                if (_centurionConnectionState == CenturionConnectionState.Disconnected)
                {
                    return true;
                }

                HidppManagerContext.Instance.SignalDeviceEvent(
                    IPCMessageType.INIT,
                    new InitMessage(deviceId, name, hasBattery, DeviceType.Headset)
                );
                RegisterKnownDevice(deviceId);
            }

            if (hasBattery && !requireFreshResponse)
            {
                initialBattery = await CreateCenturionBatteryUpdateAsync(deviceId, features, request);
                if (initialBattery == null)
                {
                    AddFailure("batteryReadFailed");
                }
            }
            else if (!hasBattery)
            {
                AddFailure("batteryFeatureMissing");
            }

            bool stillConnected;
            lock (_centurionPresenceSync)
            {
                stillConnected = _centurionConnectionState != CenturionConnectionState.Disconnected;
            }
            if (initialBattery != null && stillConnected)
            {
                RecordDeviceDiscovery("0xFF", name, DeviceType.Headset, deviceId, features, "0x0104", initialBattery.batteryPercentage.ToString("0.##"));
                ResetCenturionTransportFailures(deviceId);
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, initialBattery);
            }

            Task pollTask = Task.Run(async () =>
            {
                CancellationToken cancellationToken = _lifetimeCts.Token;
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(GlobalSettings.settings.PollPeriod), cancellationToken);
                        await UpdateCenturionBatteryAsync(deviceId, features, request);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        NativeDiagnosticsStore.RecordError($"Centurion poll failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }, _lifetimeCts.Token);
            TrackBackgroundTask(pollTask);
            ScheduleCenturionDeviceInfoLoad(deviceId, features, request);

#if DEBUG
            Console.WriteLine($"Centurion headset ready: {name} {deviceId}");
            Console.WriteLine("Centurion headset features: " + string.Join(", ", features.Select(x => $"0x{x.Key:X4}@{x.Value}")));
#endif
            return true;
        }

        private async Task<CenturionFeatureDiscovery> DiscoverCenturionFeaturesAsync(
            CenturionFeatureRequest request
        )
        {
            Dictionary<ushort, byte> features = [];
            List<CenturionFeatureDescriptor> descriptors = [];

            byte[]? root = await request(0x00, 0x00, [0x00, 0x01], this);
            if (root == null || root.Length == 0)
            {
                return new CenturionFeatureDiscovery(features, descriptors);
            }

            byte featureSetIndex = root[0];
            features[0x0001] = featureSetIndex;
            descriptors.Add(new CenturionFeatureDescriptor(
                0x0001,
                featureSetIndex,
                root.Length >= 2 ? root[1] : (byte)0,
                root.Length >= 3 ? root[2] : (byte)0
            ));

            byte[]? countResponse = await request(featureSetIndex, 0x00, [], this);
            if (countResponse == null || countResponse.Length == 0)
            {
                return new CenturionFeatureDiscovery(features, descriptors);
            }

            int featureCount = Math.Min((int)countResponse[0], 64);
            byte i = 0;
            while (i < featureCount)
            {
                byte[]? response = await request(featureSetIndex, 0x10, [i], this);
                if (response == null || response.Length < 2)
                {
                    i++;
                    continue;
                }

                IReadOnlyList<CenturionFeatureDescriptor> parsedFeatures =
                    CenturionFeatureSetCodec.DecodeEntries(response, i);
                if (parsedFeatures.Count == 0)
                {
                    i++;
                    continue;
                }

                foreach (CenturionFeatureDescriptor feature in parsedFeatures)
                {
                    if (feature.Index < featureCount)
                    {
                        features[feature.FeatureId] = feature.Index;
                        descriptors.RemoveAll(x => x.FeatureId == feature.FeatureId);
                        descriptors.Add(feature);
                    }
                }

                i = (byte)(parsedFeatures[^1].Index + 1);
            }

#if DEBUG
            Console.WriteLine("Centurion features: " + string.Join(", ", features.Select(x => $"0x{x.Key:X4}@{x.Value}")));
#endif
            return new CenturionFeatureDiscovery(features, descriptors);
        }

        private async Task<string?> ReadCenturionNameAsync(IReadOnlyDictionary<ushort, byte> features, CenturionDeviceRequest request)
        {
            if (!features.TryGetValue(0x0101, out byte nameIndex))
            {
                return null;
            }

            byte[]? response = await request(nameIndex, 0x00, []);
            if (response == null || response.Length == 0)
            {
                return null;
            }

            int nameLength = response[0];
            if (nameLength == 0)
            {
                return null;
            }

            if (response.Length >= 1 + nameLength)
            {
                return Encoding.UTF8.GetString(response.AsSpan(1, nameLength)).TrimEnd('\0');
            }

            List<byte> nameBytes = [];
            while (nameBytes.Count < nameLength)
            {
                byte[]? fragment = await request(nameIndex, 0x10, [(byte)nameBytes.Count]);
                if (fragment == null || fragment.Length == 0)
                {
                    break;
                }

                nameBytes.AddRange(fragment.Take(nameLength - nameBytes.Count));
            }

            return nameBytes.Count > 0 ? Encoding.UTF8.GetString([.. nameBytes]).TrimEnd('\0') : null;
        }

        private async Task<string?> ReadCenturionSerialAsync(IReadOnlyDictionary<ushort, byte> features, CenturionDeviceRequest request)
        {
            if (!features.TryGetValue(0x0100, out byte deviceInfoIndex))
            {
                return null;
            }

            byte[]? response = await request(deviceInfoIndex, 0x20, []);
            if (response == null || response.Length < 2)
            {
                return null;
            }

            int serialLength = Math.Min(response[0], (byte)(response.Length - 1));
            return serialLength > 0 ? Encoding.ASCII.GetString(response.AsSpan(1, serialLength)).TrimEnd('\0') : null;
        }

        private void ScheduleCenturionDeviceInfoLoad(
            string deviceId,
            IReadOnlyDictionary<ushort, byte> features,
            CenturionDeviceRequest request
        )
        {
            if (!features.TryGetValue(0x0100, out byte deviceInfoIndex))
            {
                return;
            }

            lock (_centurionPresenceSync)
            {
                if (!_centurionDeviceInfoLoads.Add(deviceId))
                {
                    return;
                }
            }

            Task loadTask = Task.Run(async () =>
            {
                bool loaded = false;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), _lifetimeCts.Token);
                    lock (_centurionPresenceSync)
                    {
                        if (_centurionConnectionState == CenturionConnectionState.Disconnected)
                        {
                            return;
                        }
                    }

                    CenturionHardwareInfo? hardware = null;
                    byte[]? hardwareResponse = await request(deviceInfoIndex, 0x00, []);
                    _ = hardwareResponse != null &&
                        CenturionDeviceInfoCodec.TryDecodeHardware(hardwareResponse, out hardware);

                    List<CenturionFirmwareInfo> firmware = [];
                    HashSet<string> seenFirmware = [];
                    for (byte index = 0; index < 8; index++)
                    {
                        byte[]? response = await request(deviceInfoIndex, 0x10, [index]);
                        if (response == null ||
                            !CenturionDeviceInfoCodec.TryDecodeFirmware(response, out CenturionFirmwareInfo? entry) ||
                            entry == null)
                        {
                            break;
                        }

                        int signatureLength = Math.Min(response.Length, 5 + response[4]);
                        string signature = Convert.ToHexString(response.AsSpan(0, signatureLength));
                        if (!seenFirmware.Add(signature))
                        {
                            break;
                        }

                        firmware.Add(entry);
                    }

                    loaded = hardware != null || firmware.Count > 0;
                    if (!loaded)
                    {
                        return;
                    }

                    NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                    {
                        x.Centurion ??= new CenturionDiscoveryDiagnostic();
                        x.Centurion.HardwareModelId = hardware == null
                            ? null
                            : NativeDiagnosticsStore.FormatHex(hardware.ModelId, 2);
                        x.Centurion.HardwareRevision = hardware == null
                            ? null
                            : NativeDiagnosticsStore.FormatHex(hardware.HardwareRevision, 2);
                        x.Centurion.HardwareProductId = hardware == null
                            ? null
                            : NativeDiagnosticsStore.FormatHex(hardware.ProductId, 4);
                        x.Centurion.Firmware = firmware
                            .Select(entry => new CenturionFirmwareDiagnostic
                            {
                                Type = NativeDiagnosticsStore.FormatHex(entry.Type, 2),
                                Name = entry.Name,
                                Version = entry.Version,
                            })
                            .ToList();
                    });
                    NativeDiagnosticsStore.AddEvent(
                        $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: loaded read-only Centurion device information"
                    );
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    NativeDiagnosticsStore.RecordError(
                        $"Centurion device info load failed: {ex.GetType().Name}: {ex.Message}"
                    );
                }
                finally
                {
                    if (!loaded)
                    {
                        lock (_centurionPresenceSync)
                        {
                            _centurionDeviceInfoLoads.Remove(deviceId);
                        }
                    }
                }
            }, _lifetimeCts.Token);
            TrackBackgroundTask(loadTask);
        }

        private async Task UpdateCenturionBatteryAsync(string deviceId, IReadOnlyDictionary<ushort, byte> features, CenturionDeviceRequest request)
        {
            UpdateMessage? update = await CreateCenturionBatteryUpdateAsync(deviceId, features, request);
            if (update != null)
            {
                ResetCenturionTransportFailures(deviceId);
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, update);
                return;
            }

            RegisterCenturionTransportFailure(deviceId, "centurionBatteryUnavailable");
        }

        private async Task<bool> ProbeCenturionPresenceAsync(
            CenturionPresenceTarget target,
            bool forcePublish
        )
        {
            UpdateMessage? update = await CreateCenturionBatteryUpdateAsync(
                target.DeviceId,
                target.Features,
                target.Request
            );
            if (update == null)
            {
                RegisterCenturionTransportFailure(target.DeviceId, "centurionPresenceUnavailable");
                return false;
            }

            bool wasOffline = ResetCenturionTransportFailures(target.DeviceId);
            if (wasOffline || forcePublish)
            {
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, update);
            }

            return true;
        }

        private void RegisterCenturionTransportFailure(string deviceId, string reason)
        {
            int failures;
            lock (_offlineSignalledDeviceIds)
            {
                failures = _centurionFailureCounts.TryGetValue(deviceId, out int existing) ? existing + 1 : 1;
                _centurionFailureCounts[deviceId] = failures;
            }
            NativeDiagnosticsStore.AddEvent(
                $"Centurion transport degraded: {reason} ({failures}/{GlobalSettings.settings.ConsecutiveFailureThreshold})."
            );
            if (DeviceTransportPolicy.ShouldSignalOffline(failures, GlobalSettings.settings.ConsecutiveFailureThreshold))
            {
                SignalOffline(deviceId);
                if (failures == GlobalSettings.settings.ConsecutiveFailureThreshold)
                {
                    RecoverTransport(reason);
                }
            }
        }

        private bool ResetCenturionTransportFailures(string deviceId)
        {
            bool wasOffline;
            lock (_offlineSignalledDeviceIds)
            {
                wasOffline = _offlineSignalledDeviceIds.Remove(deviceId);
                _centurionFailureCounts.Remove(deviceId);
            }

            NativeDiagnosticsStore.RecordCommandSuccess();
            return wasOffline;
        }

        private void SignalOffline(string deviceId, bool bypassDeferral = false)
        {
            bool wasAlreadySignalled;
            lock (_offlineSignalledDeviceIds)
            {
                wasAlreadySignalled = !_offlineSignalledDeviceIds.Add(deviceId);
                if (wasAlreadySignalled && !bypassDeferral)
                {
                    return;
                }
            }

            HidppManagerContext.Instance.SignalDeviceOffline(
                new DeviceOfflineMessage(deviceId),
                bypassDeferral,
                wasAlreadySignalled
            );
        }

        internal void SignalKnownDevicesOffline(
            string reason,
            bool bypassDeferral = false,
            IReadOnlySet<string>? stillOnlineDeviceIds = null
        )
        {
            string[] deviceIds;
            lock (_knownDeviceIds)
            {
                deviceIds = [.. _knownDeviceIds];
            }

            if (deviceIds.Length == 0)
            {
                return;
            }

            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: signalling offline for {deviceIds.Length} known device(s) after {reason}"
            );

            foreach (string deviceId in deviceIds)
            {
                if (stillOnlineDeviceIds?.Contains(deviceId) == true)
                {
                    continue;
                }

                SignalOffline(deviceId, bypassDeferral);
            }
        }

        private async Task<UpdateMessage?> CreateCenturionBatteryUpdateAsync(
            string deviceId,
            IReadOnlyDictionary<ushort, byte> features,
            CenturionDeviceRequest request
        )
        {
            if (!features.TryGetValue(0x0104, out byte batteryIndex))
            {
                return null;
            }

            byte[]? response = await request(batteryIndex, 0x00, []);
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.BatteryRawResponse = NativeDiagnosticsStore.FormatBytes(response);
            });
            if (response == null || response.Length == 0)
            {
                return null;
            }

            return CreateCenturionBatteryUpdate(deviceId, response);
        }

        private static UpdateMessage? CreateCenturionBatteryUpdate(
            string deviceId,
            ReadOnlySpan<byte> response
        )
        {
            BatteryUpdateReturn? battery = CenturionBatteryCodec.Decode(response);
            if (battery == null)
            {
                return null;
            }

            if (!double.IsFinite(battery.Value.batteryPercentage) ||
                battery.Value.batteryPercentage < 0 ||
                battery.Value.batteryPercentage > 100)
            {
                NativeDiagnosticsStore.RecordError("Rejected invalid Centurion battery percentage.");
                return null;
            }

            return new UpdateMessage(
                deviceId,
                Math.Clamp(battery.Value.batteryPercentage, 0, 100),
                battery.Value.status,
                battery.Value.batteryMVolt,
                DateTimeOffset.Now,
                -1
            );
        }

        private async Task<bool> ProbeCenturionDeviceAddressAsync()
        {
            if (_centurionReportId != CENTURION_ADDRESSED_REPORT_ID || _centurionDeviceAddress != null)
            {
                return false;
            }

            bool locked = await _centurionCommandSemaphore.WaitAsync(COMMAND_QUEUE_TIMEOUT, _lifetimeCts.Token);
            if (!locked)
            {
                return false;
            }

            try
            {
                byte[] payload = [0x00, 0x10, 0x00, 0x00, 0x00];
                for (int address = 0; address <= byte.MaxValue && !Disposed; address++)
                {
                    _centurionDeviceAddress = (byte)address;
                    _centurionProbeAttempts++;
                    await WriteCenturionCplAsync(payload);
                    byte[]? inner = await ReadCenturionInnerAsync(8);
                    if (inner is { Length: >= 2 } && inner[0] == 0x00 && (inner[1] & 0xF0) == 0x10)
                    {
                        NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                        {
                            x.Centurion ??= new CenturionDiscoveryDiagnostic();
                            x.Centurion.DeviceAddress = _centurionDeviceAddress.HasValue ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2) : null;
                            x.Centurion.ProbeAttempts = _centurionProbeAttempts;
                        });
                        return true;
                    }
                }

                _centurionDeviceAddress = null;
                return false;
            }
            finally
            {
                _centurionCommandSemaphore.Release();
            }
        }

        private async Task<byte[]?> CenturionRequestAsync(byte featureIndex, byte function, byte[] parameters, int timeout = 1000)
        {
            bool locked = await _centurionCommandSemaphore.WaitAsync(COMMAND_QUEUE_TIMEOUT, _lifetimeCts.Token);
            if (!locked)
            {
                return null;
            }

            try
            {
                byte functionSw = (byte)((function & 0xF0) | NextCenturionSwId());
                byte[] payload = [featureIndex, functionSw, .. parameters];
                return await CenturionCplRequestAsync(payload, timeout, x =>
                    x.Length >= 2 && x[0] == featureIndex && x[1] == functionSw ? x[2..] : null
                );
            }
            finally
            {
                _centurionCommandSemaphore.Release();
            }
        }

        private async Task<byte[]?> CenturionBridgeRequestAsync(byte bridgeIndex, byte subFeatureIndex, byte subFunction, byte[] parameters, int timeout = 1500)
        {
            bool locked = await _centurionCommandSemaphore.WaitAsync(COMMAND_QUEUE_TIMEOUT, _lifetimeCts.Token);
            if (!locked)
            {
                return null;
            }

            try
            {
                byte swId = NextCenturionSwId();
                byte subFunctionSw = (byte)((subFunction & 0xF0) | swId);
                byte[] subMessage = [0x00, subFeatureIndex, subFunctionSw, .. parameters];
                byte[] bridgeHeader = [(byte)((subMessage.Length >> 8) & 0x0F), (byte)(subMessage.Length & 0xFF)];
                byte[] bridgePrefix = [bridgeIndex, (byte)(0x10 | swId)];
                byte[] payload = [.. bridgePrefix, .. bridgeHeader, .. subMessage];

                bool ackReceived = false;
                DateTimeOffset started = DateTimeOffset.Now;
                await WriteCenturionCplAsync(payload);

                while ((DateTimeOffset.Now - started).TotalMilliseconds < timeout)
                {
                    byte[]? inner = await ReadCenturionInnerAsync(200);
                    if (inner == null || inner.Length < 2 || inner[0] != bridgeIndex)
                    {
                        continue;
                    }

                    byte funcSw = inner[1];
                    if ((funcSw >> 4) == 0x01 && (funcSw & 0x0F) == swId)
                    {
                        ackReceived = true;
                        break;
                    }

                    if ((funcSw >> 4) == 0x01 && (funcSw & 0x0F) == 0x00)
                    {
                        byte[]? parsed = ParseCenturionBridgeResponse(inner, subFeatureIndex, subFunctionSw);
                        if (parsed != null)
                        {
                            return parsed;
                        }
                    }
                }

                if (!ackReceived)
                {
                    return null;
                }

                while ((DateTimeOffset.Now - started).TotalMilliseconds < timeout)
                {
                    byte[]? inner = await ReadCenturionInnerAsync(200);
                    if (inner == null || inner.Length < 2 || inner[0] != bridgeIndex)
                    {
                        continue;
                    }

                    byte funcSw = inner[1];
                    if ((funcSw >> 4) == 0x01 && (funcSw & 0x0F) == 0x00)
                    {
                        byte[]? parsed = ParseCenturionBridgeResponse(inner, subFeatureIndex, subFunctionSw);
                        if (parsed != null)
                        {
                            return parsed;
                        }
                    }
                }

                return null;
            }
            finally
            {
                _centurionCommandSemaphore.Release();
            }
        }

        private static byte[]? ParseCenturionBridgeResponse(byte[] inner, byte expectedSubFeatureIndex, byte expectedSubFunctionSw)
        {
            if (inner.Length < 7)
            {
                return null;
            }

            byte subCpl = inner[4];
            byte subFeatureIndex = inner[5];
            byte subFunctionSw = inner[6];

            if (subCpl != 0x00 || subFeatureIndex != expectedSubFeatureIndex || subFunctionSw != expectedSubFunctionSw)
            {
                return null;
            }

            return inner[7..];
        }

        private async Task<byte[]?> CenturionCplRequestAsync(byte[] payload, int timeout, Func<byte[], byte[]?> tryParse)
        {
            await WriteCenturionCplAsync(payload);
            DateTimeOffset started = DateTimeOffset.Now;

            while ((DateTimeOffset.Now - started).TotalMilliseconds < timeout)
            {
                byte[]? inner = await ReadCenturionInnerAsync(200);
                if (inner == null)
                {
                    continue;
                }

                byte[]? parsed = tryParse(inner);
                if (parsed != null)
                {
                    return parsed;
                }
            }

            return null;
        }

        private async Task WriteCenturionCplAsync(byte[] payload)
        {
            byte[] frame = CenturionFrameCodec.BuildFrame(_centurionReportId, _centurionDeviceAddress, payload);
            CenturionTraceWriter.Record(
                "tx",
                _shortEndpoint.ProductId,
                _shortEndpoint.PathHash,
                frame
            );
            await _devShort.WriteAsync(frame);
        }

        private async Task<byte[]?> ReadCenturionInnerAsync(int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    byte[] frame = await _channel.Reader.ReadAsync(cts.Token);
                    if (!CenturionFrameCodec.TryExtractPayload(frame, out byte reportId, out byte? deviceAddress, out byte[] payload))
                    {
                        continue;
                    }

                    if (reportId != _centurionReportId)
                    {
                        continue;
                    }

                    if (reportId == CENTURION_ADDRESSED_REPORT_ID &&
                        deviceAddress != _centurionDeviceAddress)
                    {
                        continue;
                    }

                    return payload;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }
            }

            return null;
        }

        private byte NextCenturionSwId()
        {
            _centurionSwId++;
            if (_centurionSwId == 0 || _centurionSwId > 0x0F)
            {
                _centurionSwId = 0x01;
            }

            return _centurionSwId;
        }

        public async Task<byte[]> WriteRead10(HidDevicePtr hidDevicePtr, byte[] buffer, int timeout = DEFAULT_COMMAND_TIMEOUT)
        {
            ObjectDisposedException.ThrowIf(_disposeCount > 0, this);
            if (hidDevicePtr == IntPtr.Zero)
            {
                return [];
            }

            bool locked = await _commandSemaphore.WaitAsync(COMMAND_QUEUE_TIMEOUT, _lifetimeCts.Token);
            if (!locked)
            {
                NativeDiagnosticsStore.RecordError("HID++ 1.0 command queue timeout.");
                return [];
            }

            try
            {
                DrainResponseChannel();
                await hidDevicePtr.WriteAsync(buffer);

                using CancellationTokenSource cts = new(timeout);
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        byte[] ret = await _channel.Reader.ReadAsync(cts.Token);
                        if (ret.Length >= 4 && ret[0] == 0x10 && ret[1] == buffer[1] && ret[2] == buffer[2] && ret[3] == buffer[3])
                        {
                            NativeDiagnosticsStore.RecordCommandSuccess();
                            return ret;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                }

                return [];
            }
            finally
            {
                _commandSemaphore.Release();
            }
        }

        public async Task<Hidpp20> WriteRead20(
            HidDevicePtr hidDevicePtr,
            Hidpp20 buffer,
            int timeout = DEFAULT_COMMAND_TIMEOUT,
            bool ignoreHID10 = true,
            int? maxAttempts = null,
            bool allowC54dRecovery = true
        )
        {
            ObjectDisposedException.ThrowIf(_disposeCount > 0, this);
            if (hidDevicePtr == IntPtr.Zero)
            {
                return (Hidpp20)Array.Empty<byte>();
            }

            byte[] request = (byte[])buffer;
            bool targetsShortEndpoint = (nint)hidDevicePtr == (nint)_devShort;
            bool c54dShortRequest = ShouldUseC54dRecovery(
                targetsShortEndpoint,
                request,
                buffer,
                allowC54dRecovery
            );
            int commandTimeout = c54dShortRequest ? Math.Max(timeout, C54D_COMMAND_TIMEOUT) : timeout;
            int attempts = HidCommandAttemptPolicy.GetAttempts(c54dShortRequest, maxAttempts);
            bool locked = await _commandSemaphore.WaitAsync(COMMAND_QUEUE_TIMEOUT, _lifetimeCts.Token);
            if (!locked)
            {
                NativeDiagnosticsStore.RecordError("HID++ 2.0 command queue timeout.");
                return (Hidpp20)Array.Empty<byte>();
            }

            try
            {
                DrainResponseChannel();
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    HidDevicePtr writeDevice = targetsShortEndpoint ? _devShort : hidDevicePtr;
                    int written = await writeDevice.WriteAsync(request);
                    if (written <= 0)
                    {
                        RecordTransportFailure("hidWriteFailed", request, attempt);
                        if (c54dShortRequest)
                        {
                            Hidpp20 fallbackRet = await TryC54dLongReportFallbackAsync(buffer, request, commandTimeout, ignoreHID10, attempt);
                            if (fallbackRet.Length > 0)
                            {
                                return fallbackRet;
                            }
                        }
                        else if (targetsShortEndpoint)
                        {
                            ReopenShortEndpoint("hidWriteFailed");
                        }

                        await DelayBeforeRetry(attempt);
                        continue;
                    }

                    Hidpp20 ret = await ReadMatchingHidpp20Async(buffer, commandTimeout, ignoreHID10, c54dShortRequest);
                    if (ret.Length > 0)
                    {
                        if (c54dShortRequest && ret.GetFeatureIndex() == 0x8F)
                        {
                            RecordTransportFailure("hidProtocolError", request, attempt);
                        }

                        return ret;
                    }

                    RecordTransportFailure("hidReadTimeout", request, attempt);

                    if (c54dShortRequest)
                    {
                        ret = await TryC54dLongReportFallbackAsync(buffer, request, commandTimeout, ignoreHID10, attempt);
                        if (ret.Length > 0)
                        {
                            return ret;
                        }
                    }

                    await DelayBeforeRetry(attempt);
                }

                return (Hidpp20)Array.Empty<byte>();
            }
            finally
            {
                _commandSemaphore.Release();
            }
        }

        private async Task<Hidpp20> ReadMatchingHidpp20Async(Hidpp20 buffer, int timeout, bool ignoreHID10, bool preferNonErrorResponse = false)
        {
            using CancellationTokenSource cts = new(timeout);
            Hidpp20 firstErrorResponse = (Hidpp20)Array.Empty<byte>();
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    Hidpp20 ret = await _channel.Reader.ReadAsync(cts.Token);
                    if (ret.Length < 4 || ret.GetDeviceIdx() != buffer.GetDeviceIdx())
                    {
                        continue;
                    }

                    if (!ignoreHID10 && ret.GetFeatureIndex() == 0x8F)
                    {
                        if (!preferNonErrorResponse)
                        {
                            return ret;
                        }

                        if (firstErrorResponse.Length == 0)
                        {
                            firstErrorResponse = ret;
                        }

                        continue;
                    }

                    if (ret.GetFeatureIndex() == buffer.GetFeatureIndex() &&
                        ret.GetFunctionId() == buffer.GetFunctionId() &&
                        ret.GetSoftwareId() == buffer.GetSoftwareId())
                    {
                        NativeDiagnosticsStore.RecordCommandSuccess();
                        return ret;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }
            }

            return firstErrorResponse;
        }

        private void DrainResponseChannel()
        {
            int drained = 0;
            while (_channel.Reader.TryRead(out _))
            {
                drained++;
            }

            if (drained > 0)
            {
                NativeDiagnosticsStore.AddEvent($"Discarded {drained} stale HID responses before command.");
            }
        }

        private bool ShouldUseC54dRecovery(
            bool targetsShortEndpoint,
            byte[] request,
            Hidpp20 buffer,
            bool allowC54dRecovery
        )
        {
            return HidCommandAttemptPolicy.ShouldUseC54dRecovery(
                allowC54dRecovery,
                targetsShortEndpoint,
                _shortEndpoint.ProductId == LIGHTSPEED_C54D_RECEIVER,
                _devLong != IntPtr.Zero,
                request.Length,
                request.Length > 0 ? request[0] : (byte)0,
                buffer.GetDeviceIdx()
            );
        }

        private async Task<Hidpp20> TryC54dLongReportFallbackAsync(Hidpp20 buffer, byte[] shortRequest, int timeout, bool ignoreHID10, int attempt)
        {
            HidDevicePtr longDevice = _devLong;
            if (longDevice == IntPtr.Zero)
            {
                return (Hidpp20)Array.Empty<byte>();
            }

            byte[] longRequest = CreateLongReport(shortRequest);
            int written = await longDevice.WriteAsync(longRequest);
            if (written <= 0)
            {
                RecordTransportFailure("hidLongWriteFailed", longRequest, attempt);
                ReopenLongEndpoint("hidLongWriteFailed");
                return (Hidpp20)Array.Empty<byte>();
            }

            Hidpp20 ret = await ReadMatchingHidpp20Async(buffer, timeout, ignoreHID10, true);
            if (ret.Length == 0)
            {
                RecordTransportFailure("hidLongReadTimeout", longRequest, attempt);
                return ret;
            }

            if (ret.GetFeatureIndex() == 0x8F)
            {
                RecordTransportFailure("hidLongProtocolError", longRequest, attempt);
                return (Hidpp20)Array.Empty<byte>();
            }

            return ret;
        }

        private static byte[] CreateLongReport(byte[] shortRequest)
        {
            byte[] longRequest = new byte[20];
            longRequest[0] = 0x11;
            Array.Copy(shortRequest, 1, longRequest, 1, shortRequest.Length - 1);
            return longRequest;
        }

        private static async Task DelayBeforeRetry(int attempt)
        {
            await Task.Delay(COMMAND_RETRY_DELAY_MS * attempt);
        }

        private void ReopenShortEndpoint(string reason)
        {
            lock (_reopenSync)
            {
                ReopenShortEndpointCore(reason);
            }
        }

        private void ReopenShortEndpointCore(string reason)
        {
            if (Disposed)
            {
                return;
            }

            HidDevicePtr previous;
            lock (_handleSync)
            {
                previous = _devShort;
                _devShort = IntPtr.Zero;
            }

            MarkExpectedClose(previous);
            CloseEndpoint(previous);
            if (!JoinReader(previous))
            {
                AddFailure("reopenShortReaderStopTimeout");
                SignalKnownDevicesOffline("reopenShortReaderStopTimeout");
                return;
            }

            if (Disposed)
            {
                return;
            }

            HidDevicePtr reopened = OpenEndpoint(_shortEndpoint);
            if (reopened == IntPtr.Zero)
            {
                AddFailure("reopenShortFailed");
                SignalKnownDevicesOffline("reopenShortFailed");
                return;
            }

            lock (_handleSync)
            {
                _devShort = reopened;
            }
            if (_readCts != null)
            {
                StartReadThread(reopened, _readCts.Token);
            }

            NativeDiagnosticsStore.AddEvent($"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: reopened short endpoint after {reason}");
        }

        private void ReopenLongEndpoint(string reason)
        {
            lock (_reopenSync)
            {
                ReopenLongEndpointCore(reason);
            }
        }

        private void ReopenLongEndpointCore(string reason)
        {
            if (_longEndpoint == null || Disposed)
            {
                return;
            }

            HidDevicePtr previous;
            lock (_handleSync)
            {
                previous = _devLong;
                _devLong = IntPtr.Zero;
            }

            MarkExpectedClose(previous);
            CloseEndpoint(previous);
            if (!JoinReader(previous))
            {
                AddFailure("reopenLongReaderStopTimeout");
                return;
            }

            if (Disposed)
            {
                return;
            }

            HidDevicePtr reopened = OpenEndpoint(_longEndpoint);
            if (reopened == IntPtr.Zero)
            {
                AddFailure("reopenLongFailed");
                return;
            }

            lock (_handleSync)
            {
                _devLong = reopened;
            }
            if (_readCts != null)
            {
                StartReadThread(reopened, _readCts.Token);
            }

            NativeDiagnosticsStore.AddEvent($"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: reopened long endpoint after {reason}");
        }

        private static void CloseEndpoint(HidDevicePtr dev)
        {
            dev.Dispose();
        }

        private bool JoinReader(HidDevicePtr dev)
        {
            if (dev.SafeHandle == null)
            {
                return true;
            }

            Thread? reader;
            lock (_taskSync)
            {
                _readerThreads.TryGetValue(dev.SafeHandle, out reader);
            }

            if (reader is { IsAlive: true } && !reader.Join(THREAD_JOIN_TIMEOUT_MS))
            {
                NativeDiagnosticsStore.RecordError($"HID reader did not stop before endpoint reopen after {THREAD_JOIN_TIMEOUT_MS}ms.");
                return false;
            }

            _ = ConsumeExpectedClose(dev);
            return true;
        }

        private void MarkExpectedClose(HidDevicePtr dev)
        {
            if (dev.SafeHandle == null)
            {
                return;
            }

            lock (_handleSync)
            {
                _expectedClosedHandles.Add(dev.SafeHandle);
            }
        }

        private bool ConsumeExpectedClose(HidDevicePtr dev)
        {
            if (dev.SafeHandle == null)
            {
                return false;
            }

            lock (_handleSync)
            {
                return _expectedClosedHandles.Remove(dev.SafeHandle);
            }
        }

        private void RecordTransportFailure(string reason, byte[] request, int attempt)
        {
            AddFailure(reason);
            NativeDiagnosticsStore.AddEvent(
                $"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: {reason} attempt={attempt} request={NativeDiagnosticsStore.FormatBytes(request)}"
            );
        }

        public async Task<bool> Ping20(
            byte deviceId,
            int timeout = DEFAULT_COMMAND_TIMEOUT,
            bool ignoreHIDPP10 = true,
            int? maxAttempts = null,
            bool allowC54dRecovery = true
        )
        {
            ObjectDisposedException.ThrowIf(_disposeCount > 0, this);
            if (_devShort == IntPtr.Zero)
            {
                return false;
            }

            byte pingPayload = ++_pingPayload;
            Hidpp20 buffer = new byte[7] { 0x10, deviceId, 0x00, 0x10 | SW_ID, 0x00, 0x00, pingPayload };
            Hidpp20 ret = await WriteRead20(
                _devShort,
                buffer,
                timeout,
                ignoreHIDPP10,
                maxAttempts,
                allowC54dRecovery
            );
            if (ret.Length == 0 || ret.GetFeatureIndex() == 0x8F)
            {
                RecordPing(deviceId, false);
                return false;
            }

            bool success = ret.GetFeatureIndex() == 0x00
                && ret.GetSoftwareId() == SW_ID
                && ret.GetParam(2) == pingPayload;
            RecordPing(deviceId, success);
            return success;
        }

        internal void RecordDeviceDiscovery(
            string deviceIndex,
            string deviceName,
            DeviceType deviceType,
            string identifier,
            IReadOnlyDictionary<ushort, byte> featureMap,
            string? selectedBatteryFeature,
            string? lastBatteryResponse,
            HidppDeviceIdentity? identity = null
        )
        {
            DeviceDiscoveryDiagnostic deviceDiagnostic = new()
            {
                DeviceIndex = deviceIndex,
                DeviceName = deviceName,
                DeviceType = deviceType.ToString(),
                IdentifierHash = NativeDiagnosticsStore.HashForDiagnostics(identifier),
                Identity = identity?.ToDiagnostic(),
                FeatureMap = NativeDiagnosticsStore.FormatFeatureMap(featureMap),
                SelectedBatteryFeature = selectedBatteryFeature,
                LastBatteryResponse = lastBatteryResponse,
            };

            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                string identifierHash = NativeDiagnosticsStore.HashForDiagnostics(identifier);
                x.Devices.RemoveAll(y => y.IdentifierHash == identifierHash);
                x.Devices.Add(deviceDiagnostic);
            });
            RegisterKnownDevice(identifier);
        }

        private void RegisterKnownDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

            lock (_knownDeviceIds)
            {
                _knownDeviceIds.Add(deviceId);
            }
        }

        private void RecordPing(byte deviceId, bool success)
        {
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                x.PingResults[NativeDiagnosticsStore.FormatHex(deviceId, 2)] = success);
        }

        private void AddFailure(string reason)
        {
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                if (!x.FailureReasons.Contains(reason))
                {
                    x.FailureReasons.Add(reason);
                }
            });
            NativeDiagnosticsStore.AddEvent($"{NativeDiagnosticsStore.FormatHex(_shortEndpoint.ProductId, 4)}: {reason}");
        }
    }
}
