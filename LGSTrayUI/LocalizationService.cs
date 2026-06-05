using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace LGSTrayUI;

public sealed class LocalizationService : ObservableObject
{
    private readonly UserSettingsWrapper _settings;

    public LocalizationService(UserSettingsWrapper settings)
    {
        _settings = settings;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserSettingsWrapper.Language))
            {
                OnPropertyChanged("Item[]");
                OnPropertyChanged(nameof(CurrentLanguage));
            }
        };
    }

    public string CurrentLanguage => _settings.Language;

    public string this[string key] => Translate(key);

    public string Translate(string key)
    {
        IReadOnlyDictionary<string, string> dict = _settings.Language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? ZhCn
            : EnUs;

        return dict.TryGetValue(key, out string? value)
            ? value
            : EnUs.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    private static readonly IReadOnlyDictionary<string, string> EnUs = new Dictionary<string, string>
    {
        ["ProductName"] = "PowerTray",
        ["MenuDevices"] = "Devices",
        ["MenuRediscover"] = "Rediscover Devices",
        ["MenuNumericIcon"] = "Display Numeric Icon",
        ["MenuAutoStart"] = "Start with Windows",
        ["MenuSettings"] = "Settings",
        ["MenuExit"] = "Exit",
        ["SettingsTitle"] = "PowerTray Settings",
        ["NavGeneral"] = "General",
        ["NavAlerts"] = "Alerts",
        ["NavDevices"] = "Devices",
        ["NavDiagnostics"] = "Diagnostics",
        ["Language"] = "Language",
        ["English"] = "English",
        ["Chinese"] = "Simplified Chinese",
        ["StartWithWindows"] = "Start with Windows",
        ["NumericIcon"] = "Numeric battery icon",
        ["GlobalDefaults"] = "Global defaults",
        ["DefaultThreshold"] = "Default low battery threshold",
        ["WindowsNotification"] = "Windows notification",
        ["TrayBlink"] = "Tray icon blinking",
        ["QuietHours"] = "Quiet hours",
        ["QuietHoursEnabled"] = "Enable quiet hours",
        ["QuietStart"] = "Start",
        ["QuietEnd"] = "End",
        ["SuppressFullscreen"] = "Suppress Windows notifications while a fullscreen app is active",
        ["DeviceAlerts"] = "Device alerts",
        ["Alias"] = "Device alias",
        ["AliasHint"] = "Leave blank to use the original device name.",
        ["OriginalName"] = "Original name",
        ["Threshold"] = "Threshold",
        ["Pause"] = "Pause",
        ["PauseOneHour"] = "Pause 1 hour",
        ["PauseToday"] = "Pause today",
        ["ResumeAlerts"] = "Resume alerts",
        ["TestNotification"] = "Test notification",
        ["TestBlink"] = "Test blink",
        ["RestoreDefaults"] = "Restore defaults",
        ["LastUpdate"] = "Last update",
        ["Battery"] = "Battery",
        ["NoDevices"] = "No native devices discovered yet.",
        ["Diagnostics"] = "Diagnostics",
        ["ExportDiagnostics"] = "Export diagnostics",
        ["RefreshDiagnostics"] = "Refresh",
        ["GHubStatus"] = "G Hub running",
        ["Port9010Status"] = "localhost:9010 reachable",
        ["CurrentLanguage"] = "Current language",
        ["AlertSummary"] = "Alert summary",
        ["Yes"] = "Yes",
        ["No"] = "No",
        ["LowBatteryTitle"] = "Low battery",
        ["LowBatteryBody"] = "{0} is at {1:0}% battery.",
        ["TestNotificationTitle"] = "PowerTray test notification",
        ["TestNotificationBody"] = "Windows notification is available for {0}.",
        ["SettingsLoadErrorTitle"] = "PowerTray - Settings Load Error",
        ["SettingsLoadErrorBody"] = "Failed to read appsettings.toml. Reset it to default?",
        ["DiagnosticsExported"] = "Diagnostics exported",
        ["DiagnosticsExportFailed"] = "Failed to export diagnostics",
        ["Never"] = "Never",
        ["VersionPrefix"] = "PowerTray version",
    };

    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>
    {
        ["ProductName"] = "PowerTray",
        ["MenuDevices"] = "设备",
        ["MenuRediscover"] = "重新发现设备",
        ["MenuNumericIcon"] = "数字电量图标",
        ["MenuAutoStart"] = "开机自启",
        ["MenuSettings"] = "设置",
        ["MenuExit"] = "退出",
        ["SettingsTitle"] = "PowerTray 设置",
        ["NavGeneral"] = "常规",
        ["NavAlerts"] = "提醒",
        ["NavDevices"] = "设备",
        ["NavDiagnostics"] = "诊断",
        ["Language"] = "语言",
        ["English"] = "英语",
        ["Chinese"] = "简体中文",
        ["StartWithWindows"] = "开机自启",
        ["NumericIcon"] = "数字电量图标",
        ["GlobalDefaults"] = "全局默认",
        ["DefaultThreshold"] = "默认低电量阈值",
        ["WindowsNotification"] = "Windows 通知",
        ["TrayBlink"] = "托盘图标闪烁",
        ["QuietHours"] = "静音时段",
        ["QuietHoursEnabled"] = "启用静音时段",
        ["QuietStart"] = "开始",
        ["QuietEnd"] = "结束",
        ["SuppressFullscreen"] = "当前台有全屏软件时暂停 Windows 通知",
        ["DeviceAlerts"] = "设备提醒",
        ["Alias"] = "设备别名",
        ["AliasHint"] = "留空则使用原始设备名。",
        ["OriginalName"] = "原始名称",
        ["Threshold"] = "阈值",
        ["Pause"] = "暂停",
        ["PauseOneHour"] = "暂停 1 小时",
        ["PauseToday"] = "今天暂停",
        ["ResumeAlerts"] = "恢复提醒",
        ["TestNotification"] = "测试通知",
        ["TestBlink"] = "测试闪烁",
        ["RestoreDefaults"] = "恢复默认",
        ["LastUpdate"] = "最后更新",
        ["Battery"] = "电量",
        ["NoDevices"] = "尚未发现 native 设备。",
        ["Diagnostics"] = "诊断",
        ["ExportDiagnostics"] = "导出诊断",
        ["RefreshDiagnostics"] = "刷新",
        ["GHubStatus"] = "G Hub 正在运行",
        ["Port9010Status"] = "localhost:9010 可连接",
        ["CurrentLanguage"] = "当前语言",
        ["AlertSummary"] = "提醒配置摘要",
        ["Yes"] = "是",
        ["No"] = "否",
        ["LowBatteryTitle"] = "电量不足",
        ["LowBatteryBody"] = "{0} 当前电量为 {1:0}%。",
        ["TestNotificationTitle"] = "PowerTray 测试通知",
        ["TestNotificationBody"] = "{0} 的 Windows 通知可用。",
        ["SettingsLoadErrorTitle"] = "PowerTray - 设置加载错误",
        ["SettingsLoadErrorBody"] = "读取 appsettings.toml 失败，是否重置为默认配置？",
        ["DiagnosticsExported"] = "诊断已导出",
        ["DiagnosticsExportFailed"] = "诊断导出失败",
        ["Never"] = "从未",
        ["VersionPrefix"] = "PowerTray 版本",
    };
}
