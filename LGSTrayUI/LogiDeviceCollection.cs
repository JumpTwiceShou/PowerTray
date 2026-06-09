using LGSTrayCore;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayPrimitives;
using MessagePipe;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Windows;

namespace LGSTrayUI
{
    public class LogiDeviceCollection : ILogiDeviceCollection
    {
        private readonly UserSettingsWrapper _userSettings;
        private readonly LogiDeviceViewModelFactory _logiDeviceViewModelFactory;
        private readonly ISubscriber<IPCMessage> _subscriber;
        private readonly AppSettings _appSettings;
        private readonly AlertManager _alertManager;
        private readonly Dictionary<string, int> _missedPresenceChecks = [];
        private readonly Dictionary<string, string> _deviceIdAliases = [];
        private long _presenceEpoch;

        public ObservableCollection<LogiDeviceViewModel> Devices { get; } = [];
        public IEnumerable<LogiDevice> GetDevices() => Devices.Where(x => x.IsOnline);

        public LogiDeviceCollection(
            UserSettingsWrapper userSettings,
            LogiDeviceViewModelFactory logiDeviceViewModelFactory,
            ISubscriber<IPCMessage> subscriber,
            IOptions<AppSettings> appSettings,
            AlertManager alertManager
        )
        {
            _userSettings = userSettings;
            _logiDeviceViewModelFactory = logiDeviceViewModelFactory;
            _subscriber = subscriber;
            _appSettings = appSettings.Value;
            _alertManager = alertManager;

            _subscriber.Subscribe(x =>
            {
                if (x is InitMessage initMessage)
                {
                    OnInitMessage(initMessage);
                }
                else if (x is UpdateMessage updateMessage)
                {
                    OnUpdateMessage(updateMessage);
                }
                else if (x is DeviceOfflineMessage offlineMessage)
                {
                    OnOfflineMessage(offlineMessage);
                }
            });

            LoadPreviouslySelectedDevices();
        }

        private void LoadPreviouslySelectedDevices()
        {
            foreach (var deviceId in _userSettings.SelectedDevices)
            {
                if (string.IsNullOrEmpty(deviceId))
                {
                    continue;
                }

                if (_appSettings.Native.Enabled && !_appSettings.GHub.Enabled && deviceId.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Devices.Add(
                    _logiDeviceViewModelFactory.CreateViewModel((x) =>
                    {
                        x.DeviceId = deviceId!;
                        x.DeviceName = _userSettings.GetOriginalName(deviceId!, string.Empty);
                        x.DeviceType = _userSettings.GetDeviceType(deviceId!, x.DeviceName);
                        x.IsOnline = false;
                        x.IsChecked = true;
                    })
                );
            }
        }

        public bool TryGetDevice(string deviceId, [NotNullWhen(true)] out LogiDevice? device)
        {
            device = Devices.FirstOrDefault(x => x.IsOnline && x.DeviceId == deviceId);

            return device != null;
        }

        public void RemoveHistoricalDevice(string deviceId)
        {
            LogiDeviceViewModel? device = Devices.FirstOrDefault(x => x.DeviceId == deviceId && !x.IsOnline);
            if (device != null)
            {
                Devices.Remove(device);
            }

            _missedPresenceChecks.Remove(deviceId);
        }

        public long BeginPresenceCheck()
        {
            return Interlocked.Increment(ref _presenceEpoch);
        }

        public void CompletePresenceCheck(long epoch, int requiredMisses)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                foreach (LogiDeviceViewModel device in Devices)
                {
                    if (string.IsNullOrWhiteSpace(device.DeviceId) || device.DeviceId == LogiDevice.NOT_FOUND)
                    {
                        continue;
                    }

                    if (device.LastPresenceEpoch >= epoch)
                    {
                        _missedPresenceChecks[device.DeviceId] = 0;
                        continue;
                    }

                    int missed = _missedPresenceChecks.TryGetValue(device.DeviceId, out int existingMissed)
                        ? existingMissed + 1
                        : 1;
                    _missedPresenceChecks[device.DeviceId] = missed;

                    if (missed >= requiredMisses)
                    {
                        device.MarkOffline();
                    }
                }
            });
        }

        public void OnInitMessage(InitMessage initMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                long epoch = Interlocked.Read(ref _presenceEpoch);
                string resolvedDeviceId = ResolveDeviceId(initMessage.deviceId);
                LogiDeviceViewModel? dev = Devices.FirstOrDefault(x => x.DeviceId == resolvedDeviceId);
                if (dev != null)
                {
                    dev.UpdateState(initMessage);
                    dev.MarkPresence(epoch);
                    _missedPresenceChecks[dev.DeviceId] = 0;
                    _userSettings.MigrateUnstableDeviceHistory(dev.DeviceId, initMessage.deviceName);
                    RemoveEquivalentOfflineDuplicates(dev);

                    return;
                }

                LogiDeviceViewModel? equivalentDevice = FindEquivalentDevice(initMessage);
                if (equivalentDevice != null &&
                    (!equivalentDevice.IsOnline || ShouldMergeEquivalentOnlineDevice(equivalentDevice.DeviceId, initMessage.deviceId)))
                {
                    string oldDeviceId = equivalentDevice.DeviceId;
                    string canonicalDeviceId = ChooseCanonicalDeviceId(oldDeviceId, initMessage.deviceId);
                    if (canonicalDeviceId == initMessage.deviceId)
                    {
                        _userSettings.MigrateDeviceId(oldDeviceId, initMessage.deviceId, initMessage.deviceName, initMessage.deviceType);
                        _deviceIdAliases[oldDeviceId] = initMessage.deviceId;
                        equivalentDevice.DeviceId = initMessage.deviceId;
                    }
                    else
                    {
                        _deviceIdAliases[initMessage.deviceId] = canonicalDeviceId;
                    }

                    _missedPresenceChecks.Remove(oldDeviceId);

                    equivalentDevice.UpdateState(initMessage);
                    equivalentDevice.MarkPresence(epoch);
                    _missedPresenceChecks[equivalentDevice.DeviceId] = 0;
                    _userSettings.MigrateUnstableDeviceHistory(equivalentDevice.DeviceId, initMessage.deviceName);
                    RemoveEquivalentOfflineDuplicates(equivalentDevice);

                    return;
                }

                dev = _logiDeviceViewModelFactory.CreateViewModel((x) =>
                {
                    x.UpdateState(initMessage);
                    x.MarkPresence(epoch);
                });
                _missedPresenceChecks[dev.DeviceId] = 0;
                _userSettings.MigrateUnstableDeviceHistory(dev.DeviceId, initMessage.deviceName);
                RemoveEquivalentOfflineDuplicates(dev);

                Devices.Add(dev);
            });
        }

        private string ResolveDeviceId(string deviceId)
        {
            return _deviceIdAliases.TryGetValue(deviceId, out string? canonicalDeviceId)
                ? canonicalDeviceId
                : deviceId;
        }

        private static string ChooseCanonicalDeviceId(string existingDeviceId, string incomingDeviceId)
        {
            bool existingUnstable = IsUnstableDeviceId(existingDeviceId);
            bool incomingUnstable = IsUnstableDeviceId(incomingDeviceId);

            if (existingUnstable != incomingUnstable)
            {
                return incomingUnstable ? existingDeviceId : incomingDeviceId;
            }

            return GetDeviceIdStabilityScore(incomingDeviceId) > GetDeviceIdStabilityScore(existingDeviceId)
                ? incomingDeviceId
                : existingDeviceId;
        }

        private LogiDeviceViewModel? FindEquivalentDevice(InitMessage initMessage)
        {
            LogiDeviceViewModel[] candidates = Devices
                .Where(device => IsEquivalentDevice(device, initMessage.deviceName, initMessage.deviceType))
                .ToArray();

            return candidates.Length == 1 ? candidates[0] : null;
        }

        private void RemoveEquivalentOfflineDuplicates(LogiDeviceViewModel activeDevice)
        {
            LogiDeviceViewModel[] duplicates = Devices
                .Where(device => !device.IsOnline)
                .Where(device => device.DeviceId != activeDevice.DeviceId)
                .Where(device => IsEquivalentDevice(device, activeDevice.DeviceName, activeDevice.DeviceType))
                .ToArray();

            foreach (LogiDeviceViewModel duplicate in duplicates)
            {
                _userSettings.MigrateDeviceId(duplicate.DeviceId, activeDevice.DeviceId, activeDevice.DeviceName, activeDevice.DeviceType);
                _missedPresenceChecks.Remove(duplicate.DeviceId);
                Devices.Remove(duplicate);
            }
        }

        private static bool IsEquivalentDevice(LogiDeviceViewModel device, string deviceName, DeviceType deviceType)
        {
            if (device.DeviceType != deviceType && device.DeviceType != default)
            {
                return false;
            }

            string incomingName = NormalizeDeviceName(deviceName);
            if (string.IsNullOrEmpty(incomingName))
            {
                return false;
            }

            return NormalizeDeviceName(device.OriginalNameDisplay) == incomingName ||
                   NormalizeDeviceName(device.DeviceName) == incomingName;
        }

        private static string NormalizeDeviceName(string? deviceName) =>
            string.IsNullOrWhiteSpace(deviceName)
                ? string.Empty
                : deviceName.Trim().ToUpperInvariant();

        private static bool IsUnstableDeviceId(string deviceId) =>
            deviceId.StartsWith("fallback-", StringComparison.OrdinalIgnoreCase) ||
            deviceId.StartsWith("centurion-fallback-", StringComparison.OrdinalIgnoreCase) ||
            deviceId.StartsWith("dev", StringComparison.OrdinalIgnoreCase);

        private static bool ShouldMergeEquivalentOnlineDevice(string existingDeviceId, string incomingDeviceId) =>
            IsUnstableDeviceId(existingDeviceId) ||
            IsUnstableDeviceId(incomingDeviceId) ||
            GetDeviceIdStabilityScore(existingDeviceId) != GetDeviceIdStabilityScore(incomingDeviceId);

        private static int GetDeviceIdStabilityScore(string deviceId)
        {
            if (IsUnstableDeviceId(deviceId))
            {
                return 0;
            }

            if (deviceId.StartsWith("centurion-", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return deviceId.Contains('-', StringComparison.Ordinal)
                ? 1
                : deviceId.Length >= 12 ? 3 : 2;
        }

        public void OnUpdateMessage(UpdateMessage updateMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                string deviceId = ResolveDeviceId(updateMessage.deviceId);
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == deviceId);
                if (device == null) { return; }

                device.UpdateState(updateMessage);
                device.MarkPresence(Interlocked.Read(ref _presenceEpoch));
                _missedPresenceChecks[device.DeviceId] = 0;
                _alertManager.Evaluate(device);
            });
        }

        public void OnOfflineMessage(DeviceOfflineMessage offlineMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                string deviceId = ResolveDeviceId(offlineMessage.deviceId);
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == deviceId);
                if (device == null) { return; }

                device.MarkOffline();
                _missedPresenceChecks[device.DeviceId] = 2;
            });
        }
    }
}
