using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayHID.Features;
using System.Text;

using static LGSTrayHID.HidppDevices;

#if DEBUG
using Log = System.Console;
#else
using Log = System.Diagnostics.Debug;
#endif

namespace LGSTrayHID
{
    public class HidppDevice
    {
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private Func<HidppDevice, Task<BatteryUpdateReturn?>>? _getBatteryAsync;

        public string DeviceName { get; private set; } = string.Empty;
        public int DeviceType { get; private set; } = 3;
        public string Identifier { get; private set; } = string.Empty;

        private BatteryUpdateReturn lastBatteryReturn;
        private DateTimeOffset lastUpdate = DateTimeOffset.MinValue;
        private bool _offlineSignalled;
        private int _consecutiveFailures;
        private HidppDeviceIdentity? _identity;

        internal bool IsOffline => _offlineSignalled;

        private readonly HidppDevices _parent;
        public HidppDevices Parent => _parent;

        private readonly byte _deviceIdx;
        public byte DeviceIdx => _deviceIdx;

        private readonly Dictionary<ushort, byte> _featureMap = [];
        public Dictionary<ushort, byte> FeatureMap => _featureMap;

        public HidppDevice(HidppDevices parent, byte deviceIdx)
        {
            _parent = parent;
            _deviceIdx = deviceIdx;
        }

        private static bool HasParams(Hidpp20 message, int count)
        {
            return message.Length >= 4 + count && message.GetFeatureIndex() != 0x8F;
        }

        public async Task InitAsync()
        {
            await _initSemaphore.WaitAsync();
            try
            {
                Hidpp20 ret;

                // Sync Ping
                int successCount = 0;
                int successThresh = 3;
                for (int i = 0; i < 10; i++)
                {
                    var ping = await _parent.Ping20(_deviceIdx, 100);
                    if (ping)
                    {
                        successCount++;
                    }
                    else
                    {
                        successCount = 0;
                    }

                    if (successCount >= successThresh) { break; }
                }

                if (successCount < successThresh) { return; }

                // Find 0x0001 IFeatureSet
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, 0x00, 0x00 | SW_ID, 0x00, 0x01, 0x00 });
                if (!HasParams(ret, 1)) { return; }
                _featureMap[0x0001] = ret.GetParam(0);

                // Get Feature Count
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, _featureMap[0x0001], 0x00 | SW_ID, 0x00, 0x00, 0x00 });
                if (!HasParams(ret, 1)) { return; }
                int featureCount = Math.Min((int)ret.GetParam(0), 64);

                // Enumerate Features
                for (byte i = 0; i < featureCount; i++)
                {
                    ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, _featureMap[0x0001], 0x10 | SW_ID, i, 0x00, 0x00 });
                    if (!HasParams(ret, 2)) { continue; }
                    ushort featureId = (ushort)((ret.GetParam(0) << 8) + ret.GetParam(1));

                    _featureMap[featureId] = i;
                }

                await InitPopulateAsync();
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0018:Inline variable declaration")]
        private async Task InitPopulateAsync()
        {
            Hidpp20 ret;
            byte featureId;

            // Device name
            if (_featureMap.TryGetValue(0x0005, out featureId))
            {
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x00 | SW_ID, 0x00, 0x00, 0x00 });
                if (!HasParams(ret, 1)) { return; }
                int nameLength = ret.GetParam(0);

                List<byte> nameBytes = new(nameLength);

                while (nameBytes.Count < nameLength)
                {
                    ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x10 | SW_ID, (byte)nameBytes.Count, 0x00, 0x00 });
                    if (!HasParams(ret, 1)) { return; }

                    byte[] parameters = ret.GetParams().ToArray();
                    int bytesToRead = Math.Min(nameLength - nameBytes.Count, parameters.Length);
                    if (bytesToRead <= 0)
                    {
                        return;
                    }
                    nameBytes.AddRange(parameters[..bytesToRead].ToArray());
                }

                DeviceName = Encoding.UTF8.GetString([.. nameBytes]).TrimEnd('\0');

                foreach (var tag in GlobalSettings.settings.DisabledDevices)
                {
                    if (DeviceName.Contains(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.WriteLine($"{DeviceName} is marked as disabled");
                        return;
                    }
                };

                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x20 | SW_ID, 0x00, 0x00, 0x00 });
                if (HasParams(ret, 1))
                {
                    DeviceType = ret.GetParam(0);
                }

                DeviceType = (int)KnownLogitechDevices.GetDeviceType(DeviceName, (DeviceType)DeviceType, _parent.ProductId);
                DeviceName = KnownLogitechDevices.GetDisplayName(DeviceName, (DeviceType)DeviceType, _parent.ProductId);
            }
            else
            {
                // Device does not have a name/Hidpp error ignore it
                return;
            }

            if (_featureMap.TryGetValue(0x0003, out featureId))
            {
                byte[]? deviceInfoRawResponse;
                byte[]? deviceInfoParams = null;
                byte[]? serialRawResponse = null;
                byte[]? serialParams = null;

                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x00 | SW_ID, 0x00, 0x00, 0x00 });
                deviceInfoRawResponse = ToBytes(ret);
                if (HasParams(ret, 15))
                {
                    deviceInfoParams = ret.GetParams().ToArray();
                    if ((ret.GetParam(14) & 0x1) == 0x1)
                    {
                        ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x20 | SW_ID, 0x00, 0x00, 0x00 });
                        serialRawResponse = ToBytes(ret);
                        if (HasParams(ret, 1))
                        {
                            byte[] responseParams = ret.GetParams().ToArray();
                            serialParams = responseParams[..Math.Min(11, responseParams.Length)];
                        }
                    }
                }

                _identity = HidppDeviceIdentity.FromDeviceInformation(
                    DeviceName,
                    _parent.ProductId,
                    _deviceIdx,
                    _parent.InterfaceNumber,
                    _parent.ReceiverStableId,
                    _parent.PersistentEndpointAlias,
                    deviceInfoRawResponse,
                    deviceInfoParams,
                    serialRawResponse,
                    serialParams
                );
                Identifier = _identity.Identifier;
            }
            else
            {
                _identity = HidppDeviceIdentity.CreateFallback(
                    DeviceName,
                    _parent.ProductId,
                    _deviceIdx,
                    _parent.InterfaceNumber,
                    _parent.ReceiverStableId,
                    _parent.PersistentEndpointAlias,
                    null,
                    null,
                    "deviceInformationFeatureMissing"
                );
                Identifier = _identity.Identifier;
            }

#if DEBUG
            Log.WriteLine("---");
            Log.WriteLine(DeviceName + " Ready");
            Log.WriteLine(Identifier);
            foreach ((ushort featureIdItr, string featureDesc) in new (ushort, string)[]
            {
                (0x1000, "Battery Unified Level"),
                (0x1001, "Battery Voltage"),
                (0x1004, "Unified Battery"),
                (0x1F20, "ADC Measurement"),
            })
            {
                if (_featureMap.ContainsKey(featureIdItr))
                {
                    Log.WriteLine($"0x{featureIdItr:X} - {featureDesc} Found");
                }
            }
            Log.WriteLine("---");
#endif

            _getBatteryAsync = FeatureMap switch
            {
                { } when FeatureMap.ContainsKey(0x1000) => Battery1000.GetBatteryAsync,
                { } when FeatureMap.ContainsKey(0x1001) => Battery1001.GetBatteryAsync,
                { } when FeatureMap.ContainsKey(0x1004) => Battery1004.GetBatteryAsync,
                { } when FeatureMap.ContainsKey(0x1F20) => Battery1F20.GetBatteryAsync,
                _ => null
            };

            _parent.RecordDeviceDiscovery(
                $"0x{_deviceIdx:X2}",
                DeviceName,
                (DeviceType)DeviceType,
                Identifier,
                _featureMap,
                GetSelectedBatteryFeature(),
                null,
                _identity
            );

            SignalOnline();

            BatteryUpdateReturn? initialBattery = null;
            if (_getBatteryAsync != null)
            {
                BatteryUpdateReturn? rawInitialBattery = await ReadBatteryAsync();
                if (rawInitialBattery.HasValue && TryValidateBattery(rawInitialBattery.Value, out BatteryUpdateReturn validatedInitialBattery))
                {
                    initialBattery = validatedInitialBattery;
                    ResetTransportFailures();
                }
                if (initialBattery == null)
                {
                    _parent.RecordDeviceDiscovery(
                        $"0x{_deviceIdx:X2}",
                        DeviceName,
                        (DeviceType)DeviceType,
                        Identifier,
                        _featureMap,
                        GetSelectedBatteryFeature(),
                        "batteryReadFailed",
                        _identity
                    );
                }
            }

            if (initialBattery.HasValue)
            {
                _parent.RecordDeviceDiscovery(
                    $"0x{_deviceIdx:X2}",
                    DeviceName,
                    (DeviceType)DeviceType,
                    Identifier,
                    _featureMap,
                    GetSelectedBatteryFeature(),
                    initialBattery.Value.batteryPercentage.ToString("0.##"),
                    _identity
                );
                SignalBatteryUpdate(initialBattery.Value, true);
            }

            bool delayFirstBatteryRetry = _getBatteryAsync != null && !initialBattery.HasValue;

            Task pollTask = Task.Run(async () =>
            {
                CancellationToken cancellationToken = Parent.LifetimeToken;
                try
                {
                    if (_getBatteryAsync == null) { return; }

                    if (delayFirstBatteryRetry)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var now = DateTimeOffset.Now;
#if DEBUG
                        var expectedUpdateTime = lastUpdate.AddSeconds(1);
#else
                        var expectedUpdateTime = lastUpdate.AddSeconds(GlobalSettings.settings.PollPeriod);
#endif
                        if (now < expectedUpdateTime)
                        {
                            await Task.Delay((int)(expectedUpdateTime - now).TotalMilliseconds, cancellationToken);
                        }

                        await UpdateBattery();
                        await Task.Delay(GlobalSettings.settings.RetryTime * 1000, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    NativeDiagnosticsStore.RecordError($"Battery poll failed for {NativeDiagnosticsStore.HashForDiagnostics(Identifier)}: {ex.GetType().Name}: {ex.Message}");
                }
            }, Parent.LifetimeToken);
            Parent.TrackBackgroundTask(pollTask);
        }

        public async Task UpdateBattery(bool forceIpcUpdate = false)
        {
            if (Parent.Disposed) { return; }
            if (_getBatteryAsync == null) { return; }

            if (!await Parent.Ping20(_deviceIdx, 250, false))
            {
                RegisterTransportFailure("pingFailed");
                return;
            }

            BatteryUpdateReturn? ret = await ReadBatteryAsync();
            if (!ret.HasValue || !TryValidateBattery(ret.Value, out BatteryUpdateReturn validated))
            {
                RegisterTransportFailure(ret.HasValue ? "invalidBatteryPayload" : "batteryUnavailable");
                return;
            }

            ResetTransportFailures();
            SignalBatteryUpdate(validated, forceIpcUpdate);
        }

        private async Task<BatteryUpdateReturn?> ReadBatteryAsync()
        {
            if (Parent.Disposed) { return null; }
            if (_getBatteryAsync == null) { return null; }

            return await _getBatteryAsync.Invoke(this);
        }

        private string? GetSelectedBatteryFeature()
        {
            if (FeatureMap.ContainsKey(0x1000)) { return "0x1000"; }
            if (FeatureMap.ContainsKey(0x1001)) { return "0x1001"; }
            if (FeatureMap.ContainsKey(0x1004)) { return "0x1004"; }
            if (FeatureMap.ContainsKey(0x1F20)) { return "0x1F20"; }
            return null;
        }

        private static byte[] ToBytes(Hidpp20 message) => message.Length == 0 ? [] : (byte[])message;

        internal async Task<bool> ProbePresenceAsync(bool forcePublish = false)
        {
            if (Parent.Disposed || string.IsNullOrWhiteSpace(Identifier))
            {
                return false;
            }

            if (!await Parent.Ping20(_deviceIdx, 250, false))
            {
                RegisterTransportFailure("presencePingFailed");
                return false;
            }

            bool wasOffline = _offlineSignalled;
            ResetTransportFailures();
            if (wasOffline || forcePublish)
            {
                SignalOnline();
                BatteryUpdateReturn? battery = await ReadBatteryAsync();
                if (battery.HasValue && TryValidateBattery(battery.Value, out BatteryUpdateReturn validated))
                {
                    SignalBatteryUpdate(validated, true);
                }
            }

            return true;
        }

        private void SignalBatteryUpdate(BatteryUpdateReturn batStatus, bool forceIpcUpdate)
        {
            bool wasOffline = _offlineSignalled;
            lastUpdate = DateTimeOffset.Now;
            _offlineSignalled = false;
            _consecutiveFailures = 0;

            if (!DeviceTransportPolicy.ShouldPublishUpdate(forceIpcUpdate, wasOffline, batStatus, lastBatteryReturn))
            {
                return;
            }

            lastBatteryReturn = batStatus;
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.UPDATE,
                new UpdateMessage(Identifier, batStatus.batteryPercentage, batStatus.status, batStatus.batteryMVolt, lastUpdate)
            );
        }

        internal void SignalOnline()
        {
            if (string.IsNullOrWhiteSpace(Identifier))
            {
                return;
            }

            _offlineSignalled = false;
            _consecutiveFailures = 0;
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.INIT,
                new InitMessage(Identifier, DeviceName, _getBatteryAsync != null, (DeviceType)DeviceType)
            );
        }

        private void RegisterTransportFailure(string reason)
        {
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            NativeDiagnosticsStore.AddEvent($"Transport degraded for {NativeDiagnosticsStore.HashForDiagnostics(Identifier)}: {reason} ({failures}/{GlobalSettings.settings.ConsecutiveFailureThreshold})");
            if (DeviceTransportPolicy.ShouldSignalOffline(failures, GlobalSettings.settings.ConsecutiveFailureThreshold))
            {
                NativeDiagnosticsStore.RecordError($"Device transport offline after {failures} failures: {reason}");
                SignalOffline();
                if (failures == GlobalSettings.settings.ConsecutiveFailureThreshold)
                {
                    Parent.RecoverTransport(reason);
                }
            }
        }

        private void ResetTransportFailures()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            NativeDiagnosticsStore.RecordCommandSuccess();
        }

        private static bool TryValidateBattery(BatteryUpdateReturn value, out BatteryUpdateReturn validated)
        {
            validated = default;
            if (!double.IsFinite(value.batteryPercentage) || value.batteryPercentage < 0 || value.batteryPercentage > 100)
            {
                return false;
            }

            validated = new BatteryUpdateReturn(
                Math.Clamp(value.batteryPercentage, 0, 100),
                value.status,
                value.batteryMVolt
            );
            return true;
        }

        internal void SignalOffline()
        {
            if (_offlineSignalled)
            {
                return;
            }

            _offlineSignalled = true;
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.OFFLINE,
                new DeviceOfflineMessage(Identifier)
            );
        }
    }
}
