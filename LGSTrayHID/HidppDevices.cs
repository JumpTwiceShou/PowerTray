using LGSTrayHID.HidApi;
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

        private readonly HidEndpointInfo _shortEndpoint;
        private readonly HidEndpointInfo? _longEndpoint;
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
        private int _disposeCount;
        private int _started;

        public IReadOnlyDictionary<ushort, HidppDevice> DeviceCollection => _deviceCollection;
        public HidDevicePtr DevShort => _devShort;
        public HidDevicePtr DevLong => _devLong;
        public bool Disposed => _disposeCount > 0;

        internal HidppDevices(HidEndpointInfo shortEndpoint, HidEndpointInfo? longEndpoint)
        {
            _shortEndpoint = shortEndpoint;
            _longEndpoint = longEndpoint;
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
            if (_shortEndpoint.ProductId != 0x0AF7)
            {
                return false;
            }

            Dictionary<ushort, byte> dongleFeatures = await DiscoverCenturionFeaturesAsync(static (featureIndex, function, parameters, self) =>
                self.CenturionRequestAsync(featureIndex, function, parameters)
            );
            if (!dongleFeatures.TryGetValue(0x0003, out byte bridgeIndex))
            {
                return false;
            }

            Dictionary<ushort, byte> headsetFeatures = await DiscoverCenturionFeaturesAsync((featureIndex, function, parameters, self) =>
                self.CenturionBridgeRequestAsync(bridgeIndex, featureIndex, function, parameters)
            );
            if (headsetFeatures.Count == 0)
            {
                return false;
            }

            string name = await ReadCenturionNameAsync(bridgeIndex, headsetFeatures) ?? "PRO X 2 LIGHTSPEED";
            name = KnownLogitechDevices.GetDisplayName(name, DeviceType.Headset, _shortEndpoint.ProductId);
            string serial = await ReadCenturionSerialAsync(bridgeIndex, headsetFeatures) ?? _shortEndpoint.ProductId.ToString("X4");
            string deviceId = $"centurion-{serial}";

            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.INIT,
                new InitMessage(deviceId, name, headsetFeatures.ContainsKey(0x0104), DeviceType.Headset)
            );

            await UpdateCenturionBatteryAsync(deviceId, bridgeIndex, headsetFeatures);

            _ = Task.Run(async () =>
            {
                while (!Disposed)
                {
                    await Task.Delay(GlobalSettings.settings.PollPeriod * 1000);
                    await UpdateCenturionBatteryAsync(deviceId, bridgeIndex, headsetFeatures);
                }
            });

#if DEBUG
            Console.WriteLine($"Centurion headset ready: {name} {deviceId}");
            Console.WriteLine("Centurion headset features: " + string.Join(", ", headsetFeatures.Select(x => $"0x{x.Key:X4}@{x.Value}")));
#endif
            return true;
        }

        private delegate Task<byte[]?> CenturionFeatureRequest(byte featureIndex, byte function, byte[] parameters, HidppDevices self);

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

        private async Task<string?> ReadCenturionNameAsync(byte bridgeIndex, IReadOnlyDictionary<ushort, byte> features)
        {
            if (!features.TryGetValue(0x0101, out byte nameIndex))
            {
                return null;
            }

            byte[]? response = await CenturionBridgeRequestAsync(bridgeIndex, nameIndex, 0x00, []);
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
                byte[]? fragment = await CenturionBridgeRequestAsync(bridgeIndex, nameIndex, 0x10, [(byte)nameBytes.Count]);
                if (fragment == null || fragment.Length == 0)
                {
                    break;
                }

                nameBytes.AddRange(fragment.Take(nameLength - nameBytes.Count));
            }

            return nameBytes.Count > 0 ? Encoding.UTF8.GetString([.. nameBytes]).TrimEnd('\0') : null;
        }

        private async Task<string?> ReadCenturionSerialAsync(byte bridgeIndex, IReadOnlyDictionary<ushort, byte> features)
        {
            if (!features.TryGetValue(0x0100, out byte deviceInfoIndex))
            {
                return null;
            }

            byte[]? response = await CenturionBridgeRequestAsync(bridgeIndex, deviceInfoIndex, 0x20, []);
            if (response == null || response.Length < 2)
            {
                return null;
            }

            int serialLength = Math.Min(response[0], (byte)(response.Length - 1));
            return serialLength > 0 ? Encoding.ASCII.GetString(response.AsSpan(1, serialLength)).TrimEnd('\0') : null;
        }

        private async Task UpdateCenturionBatteryAsync(string deviceId, byte bridgeIndex, IReadOnlyDictionary<ushort, byte> features)
        {
            if (!features.TryGetValue(0x0104, out byte batteryIndex))
            {
                return;
            }

            byte[]? response = await CenturionBridgeRequestAsync(bridgeIndex, batteryIndex, 0x00, []);
            if (response == null || response.Length == 0)
            {
                return;
            }

            double batteryPercentage = response[0];
            PowerSupplyStatus status = response.Length >= 3
                ? response[2] switch
                {
                    1 or 2 => PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING,
                    3 => PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL,
                    _ => PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING,
                }
                : PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING;

            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.UPDATE,
                new UpdateMessage(deviceId, batteryPercentage, status, 0, DateTimeOffset.Now, -1)
            );
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
            byte cplLength = (byte)(payload.Length + 1);
            byte[] frame = new byte[64];
            frame[0] = 0x51;
            frame[1] = cplLength;
            frame[2] = 0x00;
            Array.Copy(payload, 0, frame, 3, Math.Min(payload.Length, frame.Length - 3));
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
                    if (frame.Length < 4 || frame[0] != 0x51)
                    {
                        continue;
                    }

                    int cplLength = frame[1];
                    if (cplLength < 1)
                    {
                        continue;
                    }

                    int payloadLength = Math.Min(cplLength - 1, frame.Length - 3);
                    if (payloadLength <= 0)
                    {
                        continue;
                    }

                    return frame[3..(3 + payloadLength)];
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
                return false;
            }

            return ret.GetFeatureIndex() == 0x00
                && ret.GetSoftwareId() == SW_ID
                && ret.GetParam(2) == pingPayload;
        }
    }
}
