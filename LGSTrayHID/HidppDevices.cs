using LGSTrayHID.HidApi;
using LGSTrayHID.Features;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using System.Text;
using System.Threading.Channels;

using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID
{
    public sealed class HidppDevices : IDisposable
    {
        public const byte SW_ID = 0x0A;

        private const int READ_TIMEOUT = 100;
        private const int DEFAULT_COMMAND_TIMEOUT = 250;
        private const byte CENTURION_REPORT_ID = CenturionFrameCodec.ReportId;
        private const byte CENTURION_ADDRESSED_REPORT_ID = CenturionFrameCodec.AddressedReportId;

        private readonly HidEndpointInfo _shortEndpoint;
        private readonly HidEndpointInfo? _longEndpoint;
        private readonly DiscoverySessionDiagnostic _diagnostics;
        private readonly Dictionary<ushort, HidppDevice> _deviceCollection = [];
        private readonly SemaphoreSlim _commandSemaphore = new(1, 1);
        private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

        private HidDevicePtr _devShort = IntPtr.Zero;
        private HidDevicePtr _devLong = IntPtr.Zero;
        private CancellationTokenSource? _readCts;
        private byte _pingPayload = 0x55;
        private byte _centurionSwId = 0x01;
        private byte _centurionReportId = CENTURION_REPORT_ID;
        private byte? _centurionDeviceAddress;
        private int _centurionProbeAttempts;
        private readonly HashSet<string> _offlineSignalledDeviceIds = [];
        private int _disposeCount;
        private int _started;

        public IReadOnlyDictionary<ushort, HidppDevice> DeviceCollection => _deviceCollection;
        public HidDevicePtr DevShort => _devShort;
        public HidDevicePtr DevLong => _devLong;
        public ushort ProductId => _shortEndpoint.ProductId;
        public int InterfaceNumber => _shortEndpoint.InterfaceNumber;
        internal string EndpointIdentityKey => $"{_shortEndpoint.SafeId}:{_shortEndpoint.PathHash}";
        internal byte CenturionReportId => _centurionReportId;
        internal byte? CenturionDeviceAddress => _centurionDeviceAddress;
        internal int CenturionProbeAttempts => _centurionProbeAttempts;
        public bool Disposed => _disposeCount > 0;

        internal HidppDevices(HidEndpointInfo shortEndpoint, HidEndpointInfo? longEndpoint)
        {
            _shortEndpoint = shortEndpoint;
            _longEndpoint = longEndpoint;
            _centurionReportId = KnownLogitechDevices.GetCenturionReportId(shortEndpoint.ProductId);
            _diagnostics = NativeDiagnosticsStore.AddSession(shortEndpoint, longEndpoint);
        }

        public async Task StartAsync()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
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

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeCount) != 1)
            {
                return;
            }

            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;
            _channel.Writer.TryComplete();
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
            Thread thread = new(() => ReadLoop(dev, cancellationToken))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
        }

        private void ReadLoop(HidDevicePtr dev, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[64];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Array.Clear(buffer);
                    int read = dev.Read(buffer, buffer.Length, READ_TIMEOUT);
                    if (read < 0)
                    {
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
                HidClose(dev);
            }
        }

        private void ProcessMessage(byte[] buffer)
        {
            if (buffer.Length < 4)
            {
                return;
            }

            if (buffer[0] == 0x10 && buffer.Length >= 7 && buffer[2] == 0x41 && (buffer[4] & 0x40) == 0)
            {
                QueueDeviceInit(buffer[1]);
                return;
            }

            _channel.Writer.TryWrite(buffer);
        }

        private void QueueDeviceInit(byte deviceIdx)
        {
            lock (_deviceCollection)
            {
                if (_deviceCollection.ContainsKey(deviceIdx))
                {
                    return;
                }

                _deviceCollection[deviceIdx] = new(this, deviceIdx);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _deviceCollection[deviceIdx].InitAsync();
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine($"Failed to initialise device index {deviceIdx}: {ex}");
#else
                    System.Diagnostics.Debug.WriteLine($"Failed to initialise device index {deviceIdx}: {ex}");
#endif
                }
            });
        }

        private async Task DiscoverDevicesAsync()
        {
            if (_devShort == IntPtr.Zero)
            {
                AddFailure("openFailed");
                return;
            }

            await Task.Delay(500);

            if (await TryDiscoverCenturionAsync())
            {
                return;
            }

            bool receiverResponded = await TryReceiverDiscoveryAsync();
            await Task.Delay(receiverResponded ? 800 : 250);

            foreach (byte deviceIdx in GetProbeDeviceIndexes(receiverResponded))
            {
                if (Disposed)
                {
                    return;
                }

                lock (_deviceCollection)
                {
                    if (_deviceCollection.ContainsKey(deviceIdx))
                    {
                        continue;
                    }
                }

                if (await Ping20(deviceIdx, DEFAULT_COMMAND_TIMEOUT, false))
                {
                    QueueDeviceInit(deviceIdx);
                }
            }
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
            byte[] ret = await WriteRead10(_devShort, [0x10, 0xFF, 0x81, 0x02, 0x00, 0x00, 0x00], 1000);
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x => x.ReceiverDiscoveryResponse = NativeDiagnosticsStore.FormatBytes(ret));
            if (ret.Length < 6 || ret[2] != 0x81 || ret[3] != 0x02)
            {
                return false;
            }

            byte numDeviceFound = ret[5];
            if (numDeviceFound > 0)
            {
                _ = await WriteRead10(_devShort, [0x10, 0xFF, 0x80, 0x02, 0x02, 0x00, 0x00], 1000);
            }

            return true;
        }

        private async Task<bool> TryDiscoverCenturionAsync()
        {
            if (!KnownLogitechDevices.IsCenturionProduct(_shortEndpoint.ProductId))
            {
                return false;
            }

            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
            });

            if (_centurionReportId == CENTURION_ADDRESSED_REPORT_ID && _centurionDeviceAddress == null)
            {
                _ = await ProbeCenturionDeviceAddressAsync();
            }

            Dictionary<ushort, byte> dongleFeatures = await DiscoverCenturionFeaturesAsync(static (featureIndex, function, parameters, self) =>
                self.CenturionRequestAsync(featureIndex, function, parameters)
            );
            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Centurion ??= new CenturionDiscoveryDiagnostic();
                x.Centurion.ReportId = NativeDiagnosticsStore.FormatHex(_centurionReportId, 2);
                x.Centurion.DeviceAddress = _centurionDeviceAddress.HasValue ? NativeDiagnosticsStore.FormatHex(_centurionDeviceAddress.Value, 2) : null;
                x.Centurion.ProbeAttempts = _centurionProbeAttempts;
                x.Centurion.DongleFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(dongleFeatures);
            });
            if (dongleFeatures.TryGetValue(0x0003, out byte bridgeIndex))
            {
                Dictionary<ushort, byte> headsetFeatures = await DiscoverCenturionFeaturesAsync((featureIndex, function, parameters, self) =>
                    self.CenturionBridgeRequestAsync(bridgeIndex, featureIndex, function, parameters)
                );
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.BridgeIndex = NativeDiagnosticsStore.FormatHex(bridgeIndex, 2);
                    x.Centurion.SubDeviceFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(headsetFeatures);
                });
                if (headsetFeatures.Count == 0)
                {
                    AddFailure("featureSetMissing");
                    return true;
                }

                return await InitialiseCenturionDeviceAsync(
                    headsetFeatures,
                    (featureIndex, function, parameters) => CenturionBridgeRequestAsync(bridgeIndex, featureIndex, function, parameters),
                    KnownLogitechDevices.GetFallbackName(DeviceType.Headset, _shortEndpoint.ProductId)
                );
            }

            if (dongleFeatures.Count > 0 && (_shortEndpoint.ProductId == 0x0B19 || dongleFeatures.ContainsKey(0x0104)))
            {
                NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
                {
                    x.Centurion ??= new CenturionDiscoveryDiagnostic();
                    x.Centurion.SubDeviceFeatureMap = NativeDiagnosticsStore.FormatFeatureMap(dongleFeatures);
                });
                return await InitialiseCenturionDeviceAsync(
                    dongleFeatures,
                    (featureIndex, function, parameters) => CenturionRequestAsync(featureIndex, function, parameters),
                    KnownLogitechDevices.GetFallbackName(DeviceType.Headset, _shortEndpoint.ProductId)
                );
            }

            AddFailure(_centurionReportId == CENTURION_ADDRESSED_REPORT_ID && _centurionDeviceAddress == null
                ? "centurionAddressUnknown"
                : "centurionBridgeMissing");
            return true;
        }

        private delegate Task<byte[]?> CenturionFeatureRequest(byte featureIndex, byte function, byte[] parameters, HidppDevices self);
        private delegate Task<byte[]?> CenturionDeviceRequest(byte featureIndex, byte function, byte[] parameters);

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
                serial = $"fallback-{_shortEndpoint.ProductId:X4}-{_shortEndpoint.InterfaceNumber}-{_shortEndpoint.PathHash}";
            }
            string deviceId = $"centurion-{serial}";
            bool hasBattery = features.ContainsKey(0x0104);
            UpdateMessage? initialBattery = null;
            if (hasBattery)
            {
                initialBattery = await CreateCenturionBatteryUpdateAsync(deviceId, features, request);
                if (initialBattery == null)
                {
                    AddFailure("batteryReadFailed");
                }
            }
            else
            {
                AddFailure("batteryFeatureMissing");
            }

            RecordDeviceDiscovery("0xFF", name, DeviceType.Headset, deviceId, features, "0x0104", initialBattery?.batteryPercentage.ToString("0.##"));
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.INIT,
                new InitMessage(deviceId, name, hasBattery, DeviceType.Headset)
            );

            if (initialBattery != null)
            {
                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, initialBattery);
            }

            _ = Task.Run(async () =>
            {
                while (!Disposed)
                {
                    await Task.Delay(GlobalSettings.settings.PollPeriod * 1000);
                    await UpdateCenturionBatteryAsync(deviceId, features, request);
                }
            });

#if DEBUG
            Console.WriteLine($"Centurion headset ready: {name} {deviceId}");
            Console.WriteLine("Centurion headset features: " + string.Join(", ", features.Select(x => $"0x{x.Key:X4}@{x.Value}")));
#endif
            return true;
        }

        private async Task<Dictionary<ushort, byte>> DiscoverCenturionFeaturesAsync(CenturionFeatureRequest request)
        {
            Dictionary<ushort, byte> features = [];

            byte[]? root = await request(0x00, 0x00, [0x00, 0x01], this);
            if (root == null || root.Length == 0)
            {
                return features;
            }

            byte featureSetIndex = root[0];
            features[0x0001] = featureSetIndex;

            byte[]? countResponse = await request(featureSetIndex, 0x00, [], this);
            if (countResponse == null || countResponse.Length == 0)
            {
                return features;
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

                IReadOnlyList<(ushort FeatureId, byte Index)> parsedFeatures = DecodeCenturionFeatureEntries(response, i);
                if (parsedFeatures.Count == 0)
                {
                    i++;
                    continue;
                }

                foreach ((ushort featureId, byte featureIndex) in parsedFeatures)
                {
                    if (featureIndex < featureCount)
                    {
                        features[featureId] = featureIndex;
                    }
                }

                i = (byte)(parsedFeatures[^1].Index + 1);
            }

#if DEBUG
            Console.WriteLine("Centurion features: " + string.Join(", ", features.Select(x => $"0x{x.Key:X4}@{x.Value}")));
#endif
            return features;
        }

        private static IReadOnlyList<(ushort FeatureId, byte Index)> DecodeCenturionFeatureEntries(byte[] response, byte startIndex)
        {
            List<(ushort FeatureId, byte Index)> features = [];

            if (response.Length >= 5)
            {
                int entryCount = Math.Min(response[0], (response.Length - 1) / 4);
                if (entryCount > 0)
                {
                    for (int i = 0; i < entryCount; i++)
                    {
                        int offset = 1 + (i * 4);
                        ushort featureId = (ushort)((response[offset] << 8) | response[offset + 1]);
                        features.Add((featureId, (byte)(startIndex + i)));
                    }

                    return features;
                }
            }

            if (response.Length >= 2)
            {
                features.Add((DecodeCenturionFeatureId(response), startIndex));
            }

            return features;
        }

        private static ushort DecodeCenturionFeatureId(byte[] response)
        {
            if (response.Length >= 3 && response[0] == 0x00)
            {
                return (ushort)((response[1] << 8) | response[2]);
            }

            return (ushort)((response[0] << 8) | response[1]);
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

        private async Task UpdateCenturionBatteryAsync(string deviceId, IReadOnlyDictionary<ushort, byte> features, CenturionDeviceRequest request)
        {
            UpdateMessage? update = await CreateCenturionBatteryUpdateAsync(deviceId, features, request);
            if (update != null)
            {
                lock (_offlineSignalledDeviceIds)
                {
                    _offlineSignalledDeviceIds.Remove(deviceId);
                }

                HidppManagerContext.Instance.SignalDeviceEvent(IPCMessageType.UPDATE, update);
                return;
            }

            SignalOffline(deviceId);
        }

        private void SignalOffline(string deviceId)
        {
            lock (_offlineSignalledDeviceIds)
            {
                if (!_offlineSignalledDeviceIds.Add(deviceId))
                {
                    return;
                }
            }

            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.OFFLINE,
                new DeviceOfflineMessage(deviceId)
            );
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

            BatteryUpdateReturn? battery = CenturionBatteryCodec.Decode(response);
            if (battery == null)
            {
                return null;
            }

            return new UpdateMessage(deviceId, battery.Value.batteryPercentage, battery.Value.status, battery.Value.batteryMVolt, DateTimeOffset.Now, -1);
        }

        private async Task<bool> ProbeCenturionDeviceAddressAsync()
        {
            if (_centurionReportId != CENTURION_ADDRESSED_REPORT_ID || _centurionDeviceAddress != null)
            {
                return false;
            }

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

        private async Task<byte[]?> CenturionRequestAsync(byte featureIndex, byte function, byte[] parameters, int timeout = 1000)
        {
            byte functionSw = (byte)((function & 0xF0) | NextCenturionSwId());
            byte[] payload = [featureIndex, functionSw, .. parameters];
            return await CenturionCplRequestAsync(payload, timeout, x =>
                x.Length >= 2 && x[0] == featureIndex && x[1] == functionSw ? x[2..] : null
            );
        }

        private async Task<byte[]?> CenturionBridgeRequestAsync(byte bridgeIndex, byte subFeatureIndex, byte subFunction, byte[] parameters, int timeout = 1500)
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

                    if (reportId == CENTURION_ADDRESSED_REPORT_ID && _centurionDeviceAddress == null)
                    {
                        _centurionDeviceAddress = deviceAddress;
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

            bool locked = await _commandSemaphore.WaitAsync(timeout);
            if (!locked)
            {
                return [];
            }

            try
            {
                await hidDevicePtr.WriteAsync(buffer);

                using CancellationTokenSource cts = new(timeout);
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        byte[] ret = await _channel.Reader.ReadAsync(cts.Token);
                        if (ret.Length >= 4 && ret[0] == 0x10 && ret[1] == buffer[1] && ret[2] == buffer[2])
                        {
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

        public async Task<Hidpp20> WriteRead20(HidDevicePtr hidDevicePtr, Hidpp20 buffer, int timeout = DEFAULT_COMMAND_TIMEOUT, bool ignoreHID10 = true)
        {
            ObjectDisposedException.ThrowIf(_disposeCount > 0, this);
            if (hidDevicePtr == IntPtr.Zero)
            {
                return (Hidpp20)Array.Empty<byte>();
            }

            byte[] request = (byte[])buffer;
            bool locked = await _commandSemaphore.WaitAsync(timeout);
            if (!locked)
            {
                return (Hidpp20)Array.Empty<byte>();
            }

            try
            {
                await hidDevicePtr.WriteAsync(request);

                using CancellationTokenSource cts = new(timeout);
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
                            return ret;
                        }

                        if (ret.GetFeatureIndex() == buffer.GetFeatureIndex() && ret.GetSoftwareId() == SW_ID)
                        {
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

                return (Hidpp20)Array.Empty<byte>();
            }
            finally
            {
                _commandSemaphore.Release();
            }
        }

        public async Task<bool> Ping20(byte deviceId, int timeout = DEFAULT_COMMAND_TIMEOUT, bool ignoreHIDPP10 = true)
        {
            ObjectDisposedException.ThrowIf(_disposeCount > 0, this);
            if (_devShort == IntPtr.Zero)
            {
                return false;
            }

            byte pingPayload = ++_pingPayload;
            Hidpp20 buffer = new byte[7] { 0x10, deviceId, 0x00, 0x10 | SW_ID, 0x00, 0x00, pingPayload };
            Hidpp20 ret = await WriteRead20(_devShort, buffer, timeout, ignoreHIDPP10);
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
                Identifier = identifier,
                Identity = identity?.ToDiagnostic(),
                FeatureMap = NativeDiagnosticsStore.FormatFeatureMap(featureMap),
                SelectedBatteryFeature = selectedBatteryFeature,
                LastBatteryResponse = lastBatteryResponse,
            };

            NativeDiagnosticsStore.UpdateSession(_diagnostics, x =>
            {
                x.Devices.RemoveAll(y => y.Identifier == identifier);
                x.Devices.Add(deviceDiagnostic);
            });
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
