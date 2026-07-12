using LGSTrayCore;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using MessagePipe;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Windows;

namespace LGSTrayUI;

public sealed class LogiDeviceCollection : ILogiDeviceCollection, IDisposable
{
    private readonly UserSettingsWrapper _userSettings;
    private readonly LogiDeviceViewModelFactory _logiDeviceViewModelFactory;
    private readonly AppSettings _appSettings;
    private readonly AlertManager _alertManager;
    private readonly IDisposable _subscription;
    private IReadOnlyList<LogiDevice> _deviceSnapshot = Array.Empty<LogiDevice>();
    private bool _disposed;

    public ObservableCollection<LogiDeviceViewModel> Devices { get; } = [];

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
        _appSettings = appSettings.Value;
        _alertManager = alertManager;

        _subscription = subscriber.Subscribe(message =>
        {
            switch (message)
            {
                case InitMessage initMessage:
                    OnInitMessage(initMessage);
                    break;
                case UpdateMessage updateMessage:
                    OnUpdateMessage(updateMessage);
                    break;
                case DeviceOfflineMessage offlineMessage:
                    OnOfflineMessage(offlineMessage);
                    break;
            }
        });

        LoadPreviouslySelectedDevices();
        RefreshSnapshot();
    }

    public IReadOnlyList<LogiDevice> GetDevices()
    {
        return Volatile.Read(ref _deviceSnapshot);
    }

    public bool TryGetDevice(string deviceId, [NotNullWhen(true)] out LogiDevice? device)
    {
        device = GetDevices().FirstOrDefault(item => item.DeviceId == deviceId);
        return device != null;
    }

    public void RemoveHistoricalDevice(string deviceId)
    {
        VerifyUiThread();
        LogiDeviceViewModel? device = Devices.FirstOrDefault(item => item.DeviceId == deviceId && !item.IsOnline);
        if (device == null)
        {
            return;
        }

        _alertManager.RemoveDeviceState(device.DeviceId);
        DetachDevice(device);
        Devices.Remove(device);
        device.Dispose();
        RefreshSnapshot();
    }

    public void OnInitMessage(InitMessage initMessage)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            LogiDeviceViewModel? existing = Devices.FirstOrDefault(item => item.DeviceId == initMessage.deviceId);
            if (existing != null)
            {
                existing.UpdateState(initMessage);
                existing.MarkPresence();
                RefreshSnapshot();
                return;
            }

            LogiDeviceViewModel created = _logiDeviceViewModelFactory.CreateViewModel(viewModel =>
            {
                viewModel.UpdateState(initMessage);
                viewModel.MarkPresence();
            });
            AttachDevice(created);
            Devices.Add(created);
            RefreshSnapshot();
        });
    }

    public void OnUpdateMessage(UpdateMessage updateMessage)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            LogiDeviceViewModel? device = Devices.FirstOrDefault(item => item.DeviceId == updateMessage.deviceId);
            if (device == null)
            {
                return;
            }

            device.UpdateState(updateMessage);
            device.MarkPresence();
            _alertManager.Evaluate(device);
            RefreshSnapshot();
        });
    }

    public void OnOfflineMessage(DeviceOfflineMessage offlineMessage)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            LogiDeviceViewModel? device = Devices.FirstOrDefault(item => item.DeviceId == offlineMessage.deviceId);
            if (device == null)
            {
                return;
            }

            device.MarkOffline();
            _alertManager.Evaluate(device);
            RefreshSnapshot();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();

        void DisposeDevices()
        {
            foreach (LogiDeviceViewModel device in Devices.ToArray())
            {
                DetachDevice(device);
                device.Dispose();
            }
            Devices.Clear();
            Volatile.Write(ref _deviceSnapshot, Array.Empty<LogiDevice>());
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            DisposeDevices();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(DisposeDevices);
        }
    }

    private void LoadPreviouslySelectedDevices()
    {
        foreach (string? deviceId in _userSettings.SelectedDevices)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                continue;
            }

            if (_appSettings.Native.Enabled && !_appSettings.GHub.Enabled &&
                deviceId.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            LogiDeviceViewModel device = _logiDeviceViewModelFactory.CreateViewModel(viewModel =>
            {
                viewModel.DeviceId = deviceId;
                viewModel.DeviceName = _userSettings.GetOriginalName(deviceId, string.Empty);
                viewModel.DeviceType = _userSettings.GetDeviceType(deviceId, viewModel.DeviceName);
                viewModel.IsOnline = false;
                viewModel.IsChecked = true;
            });
            AttachDevice(device);
            Devices.Add(device);
        }
    }

    private void AttachDevice(LogiDeviceViewModel device)
    {
        device.PropertyChanged += OnDevicePropertyChanged;
    }

    private void DetachDevice(LogiDeviceViewModel device)
    {
        device.PropertyChanged -= OnDevicePropertyChanged;
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            RefreshSnapshot();
        }
        else
        {
            _ = Application.Current.Dispatcher.BeginInvoke(RefreshSnapshot);
        }
    }

    private void RefreshSnapshot()
    {
        VerifyUiThread();
        IReadOnlyList<LogiDevice> snapshot = Devices
            .Where(device => device.IsOnline)
            .Select(device => new LogiDevice
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                DeviceType = device.DeviceType,
                HasBattery = device.HasBattery,
                BatteryPercentage = device.BatteryPercentage,
                BatteryVoltage = device.BatteryVoltage,
                BatteryMileage = device.BatteryMileage,
                PowerSupplyStatus = device.PowerSupplyStatus,
                LastUpdate = device.LastUpdate,
            })
            .ToArray();
        Volatile.Write(ref _deviceSnapshot, snapshot);
    }

    private static void VerifyUiThread()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("LogiDeviceCollection must be mutated on the WPF dispatcher thread.");
        }
    }
}
