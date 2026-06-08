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
        ["MenuRediscover"] = "Rescan devices",
        ["MenuNumericIcon"] = "Show numeric battery icon",
        ["MenuAutoStart"] = "Start with Windows",
        ["MenuSettings"] = "Settings",
        ["MenuCheckUpdates"] = "Check for updates",
        ["MenuExit"] = "Exit",
        ["SettingsTitle"] = "PowerTray Settings",
        ["NavGeneral"] = "General",
        ["NavAlerts"] = "Alerts",
        ["NavDevices"] = "Devices",
        ["NavDiagnostics"] = "Diagnostics",
        ["Language"] = "Language",
        ["English"] = "English",
        ["Chinese"] = "Simplified Chinese",
        ["Theme"] = "Theme",
        ["ThemeSystem"] = "System",
        ["ThemeLight"] = "Light",
        ["ThemeDark"] = "Dark",
        ["StartWithWindows"] = "Start with Windows",
        ["AutoCheckUpdates"] = "Check for updates automatically",
        ["NumericIcon"] = "Show battery percentage in the tray icon",
        ["GlobalDefaults"] = "Default alert settings",
        ["DefaultThreshold"] = "Default low-battery threshold",
        ["WindowsNotification"] = "Windows notifications",
        ["TrayBlink"] = "Blink tray icon",
        ["QuietHours"] = "Quiet hours",
        ["QuietHoursEnabled"] = "Enable quiet hours",
        ["QuietStart"] = "Start",
        ["QuietEnd"] = "End",
        ["SuppressFullscreen"] = "Pause Windows notifications while a full-screen app is active",
        ["DeviceAlerts"] = "Device alerts",
        ["Alias"] = "Custom device name",
        ["AliasHint"] = "Leave blank to use the device's original name.",
        ["OriginalName"] = "Original name",
        ["Offline"] = "Offline",
        ["Threshold"] = "Threshold",
        ["LowBatteryThreshold"] = "Low-battery alert threshold",
        ["FollowGlobalThreshold"] = "Use default threshold",
        ["Pause"] = "Pause",
        ["PauseOneHour"] = "Pause +1 hour",
        ["PauseUntilNextLaunch"] = "Pause until next launch",
        ["ResumeAlerts"] = "Resume alerts",
        ["TestNotification"] = "Test notification",
        ["TestBlink"] = "Test tray blink",
        ["RestoreDefaults"] = "Restore defaults",
        ["RemoveHistory"] = "Remove device history",
        ["LastUpdate"] = "Last updated",
        ["Battery"] = "Battery",
        ["NoDevices"] = "No native devices found yet.",
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
        ["LowBatteryBody"] = "{0} battery is at {1:0}%.",
        ["TestNotificationTitle"] = "PowerTray test notification",
        ["TestNotificationBody"] = "Windows notifications are working for {0}.",
        ["SettingsLoadErrorTitle"] = "PowerTray - Settings Error",
        ["SettingsLoadErrorBody"] = "Could not read appsettings.toml. Reset it to defaults?",
        ["DiagnosticsExported"] = "Diagnostics exported",
        ["DiagnosticsExportFailed"] = "Failed to export diagnostics",
        ["CheckingUpdates"] = "Checking for updates...",
        ["AlreadyLatest"] = "PowerTray is up to date.",
        ["UpdateAvailableTitle"] = "Update available",
        ["UpdateAvailableBody"] = "PowerTray {0} is available. Download the installer to your Downloads folder now?",
        ["DownloadingUpdate"] = "Downloading update...",
        ["UpdateDownloadedTitle"] = "Update downloaded",
        ["UpdateDownloadedBody"] = "The PowerTray {0} installer has been downloaded.",
        ["UpdateDownloadFailed"] = "Could not download the update",
        ["UpdateCheckFailed"] = "Could not check for updates",
        ["NoInstallerAsset"] = "The latest release does not include a PowerTray installer.",
        ["DownloadUpdate"] = "Download",
        ["RunInstaller"] = "Run installer",
        ["OpenFolder"] = "Open folder",
        ["Cancel"] = "Cancel",
        ["OK"] = "OK",
        ["Never"] = "Never",
        ["VersionPrefix"] = "PowerTray version",
        ["ReleaseVersion"] = "Installed version",
        ["PausedUntil"] = "Paused until {0}",
        ["PausedUntilNextLaunch"] = "Paused until next launch",
    };

    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>
    {
        ["ProductName"] = "PowerTray",
        ["MenuDevices"] = "设备",
        ["MenuRediscover"] = "重新扫描设备",
        ["MenuNumericIcon"] = "显示数字电量",
        ["MenuAutoStart"] = "开机自启动",
        ["MenuSettings"] = "设置",
        ["MenuCheckUpdates"] = "检查更新",
        ["MenuExit"] = "退出",
        ["SettingsTitle"] = "PowerTray 设置",
        ["NavGeneral"] = "常规",
        ["NavAlerts"] = "提醒",
        ["NavDevices"] = "设备",
        ["NavDiagnostics"] = "诊断",
        ["Language"] = "语言",
        ["English"] = "英语",
        ["Chinese"] = "简体中文",
        ["Theme"] = "主题",
        ["ThemeSystem"] = "跟随系统",
        ["ThemeLight"] = "浅色",
        ["ThemeDark"] = "深色",
        ["StartWithWindows"] = "开机自启动",
        ["AutoCheckUpdates"] = "自动检查更新",
        ["NumericIcon"] = "托盘图标显示电量百分比",
        ["GlobalDefaults"] = "默认提醒设置",
        ["DefaultThreshold"] = "默认低电量提醒阈值",
        ["WindowsNotification"] = "系统通知",
        ["TrayBlink"] = "托盘图标闪烁提醒",
        ["QuietHours"] = "静音时段",
        ["QuietHoursEnabled"] = "启用静音时段",
        ["QuietStart"] = "开始",
        ["QuietEnd"] = "结束",
        ["SuppressFullscreen"] = "全屏应用运行时暂停系统通知",
        ["DeviceAlerts"] = "设备提醒",
        ["Alias"] = "自定义设备名称",
        ["AliasHint"] = "留空则使用设备原始名称。",
        ["OriginalName"] = "原始名称",
        ["Offline"] = "离线",
        ["Threshold"] = "阈值",
        ["LowBatteryThreshold"] = "低电量提醒阈值",
        ["FollowGlobalThreshold"] = "使用默认阈值",
        ["Pause"] = "暂停",
        ["PauseOneHour"] = "暂停 +1 小时",
        ["PauseUntilNextLaunch"] = "暂停至下次启动",
        ["ResumeAlerts"] = "恢复提醒",
        ["TestNotification"] = "测试通知",
        ["TestBlink"] = "测试托盘闪烁",
        ["RestoreDefaults"] = "恢复默认",
        ["RemoveHistory"] = "删除设备记录",
        ["LastUpdate"] = "最后更新于",
        ["Battery"] = "电量",
        ["NoDevices"] = "尚未发现原生设备。",
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
        ["LowBatteryBody"] = "{0} 电量为 {1:0}%。",
        ["TestNotificationTitle"] = "PowerTray 测试通知",
        ["TestNotificationBody"] = "{0} 的系统通知可用。",
        ["SettingsLoadErrorTitle"] = "PowerTray - 设置加载错误",
        ["SettingsLoadErrorBody"] = "读取 appsettings.toml 失败，是否重置为默认配置？",
        ["DiagnosticsExported"] = "诊断已导出",
        ["DiagnosticsExportFailed"] = "诊断导出失败",
        ["CheckingUpdates"] = "正在检查更新",
        ["AlreadyLatest"] = "PowerTray 已是最新版本。",
        ["UpdateAvailableTitle"] = "发现新版本",
        ["UpdateAvailableBody"] = "PowerTray {0} 已发布。是否将安装器下载到你的下载目录？",
        ["DownloadingUpdate"] = "正在下载更新",
        ["UpdateDownloadedTitle"] = "更新已下载",
        ["UpdateDownloadedBody"] = "PowerTray {0} 已下载完成。",
        ["UpdateDownloadFailed"] = "更新下载失败",
        ["UpdateCheckFailed"] = "检查更新失败",
        ["NoInstallerAsset"] = "最新发布中没有可下载的 PowerTray 安装器。",
        ["DownloadUpdate"] = "下载",
        ["RunInstaller"] = "运行安装器",
        ["OpenFolder"] = "打开文件夹",
        ["Cancel"] = "取消",
        ["OK"] = "确定",
        ["Never"] = "从未",
        ["VersionPrefix"] = "PowerTray 版本",
        ["ReleaseVersion"] = "当前安装版本",
        ["PausedUntil"] = "暂停至 {0}",
        ["PausedUntilNextLaunch"] = "暂停至下次启动",
    };

}
