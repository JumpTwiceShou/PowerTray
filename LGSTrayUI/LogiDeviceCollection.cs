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
                        x.IsOnline = false;
                        x.IsChecked = true;
                    })
                );
            }
        }

        public bool TryGetDevice(string deviceId, [NotNullWhen(true)] out LogiDevice? device)
        {
            device = Devices.SingleOrDefault(x => x.IsOnline && x.DeviceId == deviceId);

            return device != null;
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
                LogiDeviceViewModel? dev = Devices.SingleOrDefault(x => x.DeviceId == initMessage.deviceId);
                if (dev != null)
                {
                    dev.UpdateState(initMessage);
                    dev.MarkPresence(epoch);
                    _missedPresenceChecks[dev.DeviceId] = 0;

                    return;
                }

                dev = _logiDeviceViewModelFactory.CreateViewModel((x) =>
                {
                    x.UpdateState(initMessage);
                    x.MarkPresence(epoch);
                });
                _missedPresenceChecks[dev.DeviceId] = 0;

                Devices.Add(dev);
            });
        }

        public void OnUpdateMessage(UpdateMessage updateMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == updateMessage.deviceId);
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
                var device = Devices.FirstOrDefault(dev => dev.DeviceId == offlineMessage.deviceId);
                if (device == null) { return; }

                device.MarkOffline();
                _missedPresenceChecks[device.DeviceId] = 2;
            });
        }
    }
}
