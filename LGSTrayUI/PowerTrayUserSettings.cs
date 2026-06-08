using System;
using System.Collections.Generic;

namespace LGSTrayUI;

public sealed class PowerTrayUserSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "en-US";
    public string ThemeMode { get; set; } = "system";
    public bool NumericDisplay { get; set; }
    public bool AutoStart { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public List<string> SelectedDevices { get; set; } = [];
    public AlertDefaults GlobalAlerts { get; set; } = new();
    public Dictionary<string, DeviceAlertSettings> Devices { get; set; } = [];
}

public sealed class AlertDefaults
{
    public int ThresholdPercent { get; set; } = 15;
    public bool WindowsNotification { get; set; } = true;
    public bool TrayBlink { get; set; } = true;
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStart { get; set; } = "23:00";
    public string QuietHoursEnd { get; set; } = "08:00";
    public bool SuppressNotificationsWhenFullscreen { get; set; } = true;
}

public sealed class DeviceAlertSettings
{
    public string Alias { get; set; } = string.Empty;
    public string LastDeviceName { get; set; } = string.Empty;
    public int? ThresholdPercent { get; set; }
    public bool? WindowsNotification { get; set; }
    public bool? TrayBlink { get; set; }
    public DateTimeOffset? PauseUntil { get; set; }
}
