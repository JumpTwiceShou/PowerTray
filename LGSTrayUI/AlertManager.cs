using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace LGSTrayUI;

public sealed class AlertManager
{
    private readonly UserSettingsWrapper _settings;
    private readonly AlertStateService _alertState;
    private readonly NotificationService _notifications;
    private readonly SystemStateService _systemState;
    private readonly Dictionary<string, RuntimeAlertState> _runtime = [];
    private ObservableCollection<LogiDeviceViewModel>? _devices;
    private readonly DispatcherTimer _timer;

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
        _settings.DeviceSettingsChanged += _ => EvaluateAll();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _timer.Tick += (_, _) => EvaluateAll();
        _timer.Start();
    }

    public void SetDevices(ObservableCollection<LogiDeviceViewModel> devices)
    {
        _devices = devices;
        foreach (LogiDeviceViewModel device in devices)
        {
            Evaluate(device);
        }
        devices.CollectionChanged += (_, _) => EvaluateAll();
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

        _settings.GetDeviceSettings(device.DeviceId, device.DeviceName);
        RuntimeAlertState state = GetRuntimeState(device.DeviceId);
        DeviceAlertSettings deviceSettings = _settings.GetDeviceSettings(device.DeviceId);
        DateTimeOffset now = DateTimeOffset.Now;

        bool paused = deviceSettings.PauseUntil.HasValue && deviceSettings.PauseUntil.Value > now;
        bool lowBattery = device.HasBattery &&
                          device.BatteryPercentage >= 0 &&
                          device.BatteryPercentage < _settings.GetThreshold(device.DeviceId) &&
                          device.PowerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING &&
                          device.PowerSupplyStatus != PowerSupplyStatus.POWER_SUPPLY_STATUS_FULL;

        if (!lowBattery || paused)
        {
            state.HasNotifiedThisCycle = false;
            state.NotificationPending = false;
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
            state.NotificationPending = true;
            return;
        }

        _notifications.ShowLowBattery(device);
        state.HasNotifiedThisCycle = true;
        state.NotificationPending = false;
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
        public bool NotificationPending { get; set; }
    }
}
