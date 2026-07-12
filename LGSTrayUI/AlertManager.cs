using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Threading;

namespace LGSTrayUI;

public sealed class AlertManager : IDisposable
{
    private readonly UserSettingsWrapper _settings;
    private readonly AlertStateService _alertState;
    private readonly NotificationService _notifications;
    private readonly SystemStateService _systemState;
    private readonly Dictionary<string, RuntimeAlertState> _runtime = [];
    private readonly DispatcherTimer _timer;
    private readonly Action<string> _settingsChangedHandler;

    private ObservableCollection<LogiDeviceViewModel>? _devices;
    private bool _disposed;

    public AlertManager(
        UserSettingsWrapper settings,
        AlertStateService alertState,
        NotificationService notifications,
        SystemStateService systemState
    )
    {
        _settings = settings;
        _alertState = alertState;
        _notifications = notifications;
        _systemState = systemState;
        _settingsChangedHandler = _ => EvaluateAll();
        _settings.DeviceSettingsChanged += _settingsChangedHandler;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public void SetDevices(ObservableCollection<LogiDeviceViewModel> devices)
    {
        if (_devices != null)
        {
            _devices.CollectionChanged -= OnDevicesCollectionChanged;
        }

        _devices = devices;
        foreach (LogiDeviceViewModel device in devices)
        {
            Evaluate(device);
        }
        devices.CollectionChanged += OnDevicesCollectionChanged;
    }

    public void EvaluateAll()
    {
        if (_devices == null)
        {
            return;
        }

        foreach (LogiDeviceViewModel device in _devices.ToArray())
        {
            Evaluate(device);
        }
    }

    public void Evaluate(LogiDeviceViewModel device)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceId) || device.DeviceId == LGSTrayCore.LogiDevice.NOT_FOUND)
        {
            return;
        }

        RuntimeAlertState state = GetRuntimeState(device.DeviceId);
        if (!device.IsOnline)
        {
            state.HasNotifiedThisCycle = false;
            state.WasSuppressed = false;
            _alertState.SetBlinking(device.DeviceId, false);
            return;
        }

        _settings.GetDeviceSettings(device.DeviceId, device.DeviceName);
        DateTimeOffset now = DateTimeOffset.Now;
        bool paused = _settings.IsDevicePaused(device.DeviceId, now);
        bool lowBattery = device.HasBattery &&
                          device.BatteryPercentage >= 0 &&
                          device.BatteryPercentage <= _settings.GetThreshold(device.DeviceId) &&
                          device.PowerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING &&
                          device.PowerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL;

        if (!lowBattery || paused)
        {
            state.HasNotifiedThisCycle = false;
            state.WasSuppressed = false;
            _alertState.SetBlinking(device.DeviceId, false);
            return;
        }

        _alertState.SetBlinking(device.DeviceId, _settings.GetTrayBlinkEnabled(device.DeviceId));
        if (!_settings.GetWindowsNotificationEnabled(device.DeviceId) || state.HasNotifiedThisCycle)
        {
            return;
        }

        bool suppressNotification = IsQuietHoursActive() ||
                                    (_settings.SuppressNotificationsWhenFullscreen && _systemState.IsForegroundFullscreen());
        if (suppressNotification)
        {
            state.WasSuppressed = true;
            return;
        }

        _notifications.ShowLowBattery(device);
        state.HasNotifiedThisCycle = true;
        state.WasSuppressed = false;
    }

    public void RemoveDeviceState(string deviceId)
    {
        _runtime.Remove(deviceId);
        _alertState.Remove(deviceId);
    }

    public void MigrateDeviceState(string oldDeviceId, string newDeviceId)
    {
        if (string.Equals(oldDeviceId, newDeviceId, StringComparison.Ordinal))
        {
            return;
        }

        if (_runtime.Remove(oldDeviceId, out RuntimeAlertState? existing))
        {
            _runtime[newDeviceId] = existing;
        }
        _alertState.Migrate(oldDeviceId, newDeviceId);
    }

    public void TestBlink(LogiDeviceViewModel device)
    {
        _alertState.SetBlinking(device.DeviceId, true);
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Evaluate(device);
        };
        timer.Start();
    }

    public void TestBlinkAll(IEnumerable<LogiDeviceViewModel> devices)
    {
        LogiDeviceViewModel[] targets = devices
            .Where(device => device.IsOnline)
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId) &&
                             device.DeviceId != LGSTrayCore.LogiDevice.NOT_FOUND)
            .ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        foreach (LogiDeviceViewModel device in targets)
        {
            _alertState.SetBlinking(device.DeviceId, true);
        }

        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (LogiDeviceViewModel device in targets)
            {
                Evaluate(device);
            }
        };
        timer.Start();
    }

    public void StopBlinking()
    {
        _alertState.ClearAll();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _settings.DeviceSettingsChanged -= _settingsChangedHandler;
        if (_devices != null)
        {
            _devices.CollectionChanged -= OnDevicesCollectionChanged;
            _devices = null;
        }
        _runtime.Clear();
        _alertState.ClearAll();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        EvaluateAll();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (LogiDeviceViewModel device in e.OldItems.OfType<LogiDeviceViewModel>())
            {
                RemoveDeviceState(device.DeviceId);
            }
        }
        EvaluateAll();
    }

    private RuntimeAlertState GetRuntimeState(string deviceId)
    {
        if (!_runtime.TryGetValue(deviceId, out RuntimeAlertState? state))
        {
            state = new();
            _runtime[deviceId] = state;
        }
        return state;
    }

    private bool IsQuietHoursActive()
    {
        if (!_settings.QuietHoursEnabled ||
            !TimeSpan.TryParse(_settings.QuietHoursStart, out TimeSpan start) ||
            !TimeSpan.TryParse(_settings.QuietHoursEnd, out TimeSpan end))
        {
            return false;
        }

        TimeSpan now = DateTime.Now.TimeOfDay;
        return start <= end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    private sealed class RuntimeAlertState
    {
        public bool HasNotifiedThisCycle { get; set; }
        public bool WasSuppressed { get; set; }
    }
}
