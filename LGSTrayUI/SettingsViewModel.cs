using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LGSTrayCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Extensions.Options;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayCore.Managers;
using LGSTrayCore.HttpServer;

namespace LGSTrayUI;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsWrapper _settings;
    private readonly LogiDeviceCollection _deviceCollection;
    private readonly NotificationService _notifications;
    private readonly AlertManager _alertManager;
    private readonly SystemStateService _systemState;
    private readonly UpdateService _updateService;
    private readonly NativeDiagnosticsClient _nativeDiagnosticsClient;
    private readonly NativeBackendStatus _nativeBackendStatus;
    private readonly HttpServerStatus _httpServerStatus;
    private readonly AppSettings _appSettings;
    private double _uiScaleValue;
    private int _defaultThresholdDraft;
    private bool _isEditingDefaultThreshold;
    private bool _lastGHubRunning;
    private bool _lastPort9010Reachable;

    public LocalizationService Loc { get; }
    public ObservableCollection<DeviceSettingsItemViewModel> DeviceItems { get; } = [];
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new("en-US", "English"),
        new("zh-CN", "简体中文"),
        new("ja-JP", "日本語"),
    ];

    public IReadOnlyList<UiScaleOption> UiScaleOptions { get; } =
    [
        new(0, "small", 0.94, "UiScaleSmall"),
        new(1, "standard", 1.00, "UiScaleStandard"),
        new(2, "large", 1.12, "UiScaleLarge"),
        new(3, "maximum", 1.25, "UiScaleMaximum"),
    ];

    [ObservableProperty]
    private string _gHubStatus = string.Empty;

    [ObservableProperty]
    private string _port9010Status = string.Empty;

    [ObservableProperty]
    private string _diagnosticSummary = string.Empty;

    public string CurrentVersion => GetCurrentVersion();

    public SettingsViewModel(
        UserSettingsWrapper settings,
        ILogiDeviceCollection deviceCollection,
        LocalizationService loc,
        NotificationService notifications,
        AlertManager alertManager,
        SystemStateService systemState,
        UpdateService updateService,
        NativeDiagnosticsClient nativeDiagnosticsClient,
        NativeBackendStatus nativeBackendStatus,
        HttpServerStatus httpServerStatus,
        IOptions<AppSettings> appSettings
    )
    {
        _settings = settings;
        _deviceCollection = (LogiDeviceCollection)deviceCollection;
        _notifications = notifications;
        _alertManager = alertManager;
        _systemState = systemState;
        _updateService = updateService;
        _nativeDiagnosticsClient = nativeDiagnosticsClient;
        _nativeBackendStatus = nativeBackendStatus;
        _httpServerStatus = httpServerStatus;
        _appSettings = appSettings.Value;
        Loc = loc;
        _uiScaleValue = CurrentUiScaleOption.Index;
        _defaultThresholdDraft = _settings.DefaultThresholdPercent;

        _deviceCollection.Devices.CollectionChanged += OnDevicesChanged;
        _settings.DeviceSettingsChanged += OnDeviceSettingsChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        Loc.PropertyChanged += (_, _) => RefreshBindings();
        RefreshDeviceItems();
        ObserveTask(RefreshDiagnosticsAsync(), "initial diagnostics refresh");
    }

    public string Language
    {
        get => _settings.Language;
        set
        {
            _settings.Language = value;
            RefreshBindings();
        }
    }

    public string ThemeMode
    {
        get => _settings.ThemeMode;
        set
        {
            _settings.ThemeMode = value;
            RefreshBindings();
        }
    }

    public double UiScaleValue
    {
        get => _uiScaleValue;
        set
        {
            double normalized = Math.Clamp(value, UiScaleOptions[0].Index, UiScaleOptions[^1].Index);
            if (Math.Abs(_uiScaleValue - normalized) < 0.0001)
            {
                return;
            }

            _uiScaleValue = normalized;
            OnPropertyChanged();
        }
    }

    public string SelectedUiScaleCode => CurrentUiScaleOption.Code;

    public double WindowWidth => GetWindowSize().Width;
    public double WindowHeight => GetWindowSize().Height;
    public double WindowMinWidth => GetWindowMinimumWidth();
    public double WindowMinHeight => Math.Min(WindowHeight, 620);

    public void CommitUiScaleValue()
    {
        UiScaleOption option = UiScaleOptions
            .OrderBy(x => Math.Abs(x.Index - _uiScaleValue))
            .First();

        if (Math.Abs(_uiScaleValue - option.Index) >= 0.0001)
        {
            _uiScaleValue = option.Index;
            OnPropertyChanged(nameof(UiScaleValue));
        }

        if (_settings.UiScaleMode == option.Code)
        {
            return;
        }

        _settings.UiScaleMode = option.Code;
        RefreshBindings();
    }

    public bool AutoStart
    {
        get => _settings.AutoStart;
        set
        {
            _settings.AutoStart = value;
            RefreshBindings();
        }
    }

    public bool AutoCheckUpdates
    {
        get => _settings.AutoCheckUpdates;
        set
        {
            _settings.AutoCheckUpdates = value;
            RefreshBindings();
        }
    }

    public bool NumericDisplay
    {
        get => _settings.NumericDisplay;
        set
        {
            _settings.NumericDisplay = value;
            RefreshBindings();
        }
    }

    public int DefaultThresholdPercent
    {
        get => _defaultThresholdDraft;
        set
        {
            int threshold = Math.Clamp(value, 1, 100);
            if (_defaultThresholdDraft == threshold)
            {
                return;
            }

            _defaultThresholdDraft = threshold;
            _isEditingDefaultThreshold = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultThresholdPercentText));
        }
    }

    public string DefaultThresholdPercentText => $"{DefaultThresholdPercent}%";

    public void CommitDefaultThresholdPercent()
    {
        int threshold = Math.Clamp(_defaultThresholdDraft, 1, 100);
        _isEditingDefaultThreshold = false;
        if (_settings.DefaultThresholdPercent == threshold)
        {
            _defaultThresholdDraft = threshold;
            OnPropertyChanged(nameof(DefaultThresholdPercent));
            OnPropertyChanged(nameof(DefaultThresholdPercentText));
            return;
        }

        _settings.DefaultThresholdPercent = threshold;
    }

    public bool DefaultWindowsNotification
    {
        get => _settings.DefaultWindowsNotification;
        set
        {
            _settings.DefaultWindowsNotification = value;
            RefreshBindings();
        }
    }

    public bool DefaultTrayBlink
    {
        get => _settings.DefaultTrayBlink;
        set
        {
            _settings.DefaultTrayBlink = value;
            RefreshBindings();
        }
    }

    public bool QuietHoursEnabled
    {
        get => _settings.QuietHoursEnabled;
        set
        {
            _settings.QuietHoursEnabled = value;
            RefreshBindings();
        }
    }

    public string QuietHoursStart
    {
        get => _settings.QuietHoursStart;
        set
        {
            _settings.QuietHoursStart = value;
            RefreshBindings();
        }
    }

    public string QuietHoursEnd
    {
        get => _settings.QuietHoursEnd;
        set
        {
            _settings.QuietHoursEnd = value;
            RefreshBindings();
        }
    }

    public bool SuppressNotificationsWhenFullscreen
    {
        get => _settings.SuppressNotificationsWhenFullscreen;
        set
        {
            _settings.SuppressNotificationsWhenFullscreen = value;
            RefreshBindings();
        }
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        _lastGHubRunning = _systemState.IsGHubRunning();
        _lastPort9010Reachable = await _systemState.IsPort9010ReachableAsync();
        RefreshStatusText();
        DiagnosticSummary = BuildDiagnostics();
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        await RefreshDiagnosticsAsync();
        NativeDiagnosticsResponseMessage? nativeResponse = await _nativeDiagnosticsClient.RequestAsync(TimeSpan.FromSeconds(2));
        string? nativeError = nativeResponse == null ? Loc["HidNoResponse"] : nativeResponse.error;
        JsonObject diagnosticsJson = BuildDiagnosticsJson(nativeResponse, nativeError);
        string summary = BuildDiagnosticsSummary(nativeResponse, nativeError);
        string readme = BuildDiagnosticsReadme();

        SaveFileDialog dialog = new()
        {
            Filter = "Zip archives (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = $"PowerTray-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using FileStream file = File.Create(dialog.FileName);
            using ZipArchive archive = new(file, ZipArchiveMode.Create);
            WriteZipEntry(archive, "summary.txt", summary);
            WriteZipEntry(archive, "diagnostics.json", diagnosticsJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            WriteZipEntry(archive, "readme.txt", readme);
            DiagnosticSummary = summary;
            _notifications.Show(Loc["DiagnosticsExported"], dialog.FileName);
        }
        catch (Exception ex)
        {
            _notifications.Show(Loc["DiagnosticsExportFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await _updateService.CheckForUpdatesAsync();
    }

    [RelayCommand]
    private void TestNotification()
    {
        _notifications.ShowTest();
    }

    [RelayCommand]
    private void TestBlinkAll()
    {
        _alertManager.TestBlinkAll(_deviceCollection.Devices.Where(device => device.IsChecked && device.IsOnline));
    }

    [RelayCommand]
    private void StopBlink()
    {
        _alertManager.StopBlinking();
    }

    private static void ObserveTask(Task task, string context)
    {
        _ = task.ContinueWith(completed =>
        {
            if (completed.IsFaulted && completed.Exception != null)
            {
                Debug.WriteLine($"Settings {context} failed: {completed.Exception.GetBaseException()}");
            }
        }, TaskScheduler.Default);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDeviceItems();
    }

    private void OnDeviceSettingsChanged(string deviceId)
    {
        bool refreshDiagnostics = true;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            foreach (DeviceSettingsItemViewModel item in DeviceItems)
            {
                item.Refresh();
            }
        }
        else
        {
            DeviceSettingsItemViewModel? item = DeviceItems.FirstOrDefault(x => x.DeviceId == deviceId);
            item?.Refresh();
            refreshDiagnostics = item is not { IsEditingThreshold: true };
        }

        if (refreshDiagnostics)
        {
            DiagnosticSummary = BuildDiagnostics();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsWrapper.Snapshot))
        {
            DiagnosticSummary = BuildDiagnostics();
            return;
        }

        RefreshBindings();
    }

    private void RefreshDeviceItems()
    {
        foreach (DeviceSettingsItemViewModel item in DeviceItems)
        {
            item.Dispose();
        }

        DeviceItems.Clear();
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices.Where(x => x.DeviceName != LogiDevice.NOT_FOUND))
        {
            DeviceItems.Add(new(device, _settings, Loc, _notifications, _alertManager, RemoveDeviceHistory));
        }
        DiagnosticSummary = BuildDiagnostics();
    }

    private void RemoveDeviceHistory(DeviceSettingsItemViewModel item)
    {
        _settings.RemoveDeviceHistory(item.DeviceId);
        _deviceCollection.RemoveHistoricalDevice(item.DeviceId);
        RefreshDeviceItems();
    }

    private void RefreshBindings()
    {
        _uiScaleValue = CurrentUiScaleOption.Index;
        if (!_isEditingDefaultThreshold)
        {
            _defaultThresholdDraft = _settings.DefaultThresholdPercent;
        }

        RefreshStatusText();
        OnPropertyChanged(string.Empty);
        foreach (DeviceSettingsItemViewModel item in DeviceItems)
        {
            item.Refresh();
        }
        DiagnosticSummary = BuildDiagnostics();
    }

    private UiScaleOption CurrentUiScaleOption =>
        UiScaleOptions.FirstOrDefault(x => x.Code.Equals(_settings.UiScaleMode, StringComparison.OrdinalIgnoreCase))
        ?? UiScaleOptions[1];

    private (double Width, double Height) GetWindowSize()
    {
        (double width, double height) = CurrentUiScaleOption.Code switch
        {
            "small" => (880.0, 670.0),
            "large" => (990.0, 785.0),
            "maximum" => (1100.0, 875.0),
            _ => (880.0, 700.0),
        };

        Rect workArea = SystemParameters.WorkArea;
        return (
            CapToWorkArea(width, workArea.Width),
            CapToWorkArea(height, workArea.Height)
        );
    }

    private static double CapToWorkArea(double desired, double available)
    {
        if (available <= 0)
        {
            return desired;
        }

        double cap = Math.Max(360.0, available - 32.0);
        return Math.Round(Math.Min(desired, cap));
    }

    private double GetWindowMinimumWidth()
    {
        double desired = CurrentUiScaleOption.Code switch
        {
            "large" => 990.0,
            "maximum" => 1100.0,
            _ => 880.0,
        };

        return CapToWorkArea(desired, SystemParameters.WorkArea.Width);
    }

    private void RefreshStatusText()
    {
        GHubStatus = _lastGHubRunning ? Loc["Yes"] : Loc["No"];
        Port9010Status = _lastPort9010Reachable ? Loc["Yes"] : Loc["No"];
    }

    private string BuildDiagnostics()
    {
        StringBuilder sb = new();
        sb.AppendLine(Loc["DiagnosticsTitle"]);
        sb.AppendLine($"{Loc["ReleaseVersion"]}: {CurrentVersion}");
        sb.AppendLine($"{Loc["DiagnosticsGenerated"]}: {DateTimeOffset.Now:o}");
        sb.AppendLine($"{Loc["CurrentLanguage"]}: {Language}");
        sb.AppendLine($"{Loc["Theme"]}: {ThemeMode}");
        sb.AppendLine($"{Loc["GHubStatus"]}: {GHubStatus}");
        sb.AppendLine($"{Loc["Port9010Status"]}: {Port9010Status}");
        sb.AppendLine();
        sb.AppendLine(Loc["AlertSummary"]);
        sb.AppendLine(BuildSettingsDiagnosticsSummary().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();
        sb.AppendLine($"{Loc["DiagnosticsDevices"]}:");
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            sb.AppendLine($"{HashForDiagnostics(device.DeviceId)} | {device.DeviceName} | {device.DeviceType} | {device.BatteryPercentage:0.00}% | {device.PowerSupplyStatus} | {device.LastUpdate:o}");
        }
        return sb.ToString();
    }

    private JsonObject BuildDiagnosticsJson(NativeDiagnosticsResponseMessage? nativeResponse, string? nativeError)
    {
        JsonNode? nativeNode = null;
        if (!string.IsNullOrWhiteSpace(nativeResponse?.diagnosticsJson))
        {
            try
            {
                nativeNode = JsonNode.Parse(nativeResponse.diagnosticsJson);
            }
            catch
            {
                nativeError = Loc["HidJsonParseFailed"];
            }
        }

        JsonObject root = new()
        {
            ["schemaVersion"] = 2,
            ["generatedAt"] = DateTimeOffset.Now.ToString("o"),
            ["appVersion"] = CurrentVersion,
            ["system"] = BuildSystemJson(),
            ["appSettings"] = BuildAppSettingsJson(),
            ["hidEnumeration"] = CloneOrEmptyArray(nativeNode?["hidEnumeration"]),
            ["unsupportedHidDevices"] = CloneOrEmptyArray(nativeNode?["unsupportedHidDevices"]),
            ["nativeDiscovery"] = CloneOrEmptyArray(nativeNode?["nativeDiscovery"]),
            ["recognizedDevices"] = BuildRecognizedDevicesJson(),
            ["recentEvents"] = CloneOrEmptyArray(nativeNode?["recentEvents"]),
            ["nativeBackend"] = BuildNativeBackendJson(),
            ["httpServer"] = BuildHttpServerJson(),
        };

        if (!string.IsNullOrWhiteSpace(nativeError))
        {
            root["nativeDiagnosticsError"] = nativeError;
        }

        return root;
    }

    private JsonObject BuildSystemJson()
    {
        return new JsonObject
        {
            ["windowsVersion"] = Environment.OSVersion.VersionString,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
            ["is64BitProcess"] = Environment.Is64BitProcess,
            ["currentLanguage"] = Language,
            ["gHubRunning"] = _lastGHubRunning,
            ["port9010Reachable"] = _lastPort9010Reachable,
        };
    }

    private JsonObject BuildNativeBackendJson()
    {
        NativeBackendStatusSnapshot status = _nativeBackendStatus.Snapshot;
        return new JsonObject
        {
            ["state"] = status.State,
            ["helperProcessId"] = status.HelperProcessId,
            ["helperStartedAt"] = status.HelperStartedAt?.ToString("o"),
            ["lastHeartbeatAt"] = status.LastHeartbeatAt?.ToString("o"),
            ["lastSuccessfulCommandAt"] = status.LastSuccessfulCommandAt?.ToString("o"),
            ["lastError"] = status.LastError,
            ["restartCount"] = status.RestartCount,
        };
    }

    private JsonObject BuildHttpServerJson()
    {
        HttpServerStatusSnapshot status = _httpServerStatus.Snapshot;
        return new JsonObject
        {
            ["state"] = status.State,
            ["startedAt"] = status.StartedAt?.ToString("o"),
            ["lastError"] = status.LastError,
            ["restartCount"] = status.RestartCount,
            ["remoteBinding"] = _appSettings.HTTPServer.RequiresAuthentication,
        };
    }

    private JsonObject BuildAppSettingsJson()
    {
        return new JsonObject
        {
            ["installerEdition"] = UpdateService.GetInstalledInstallerEdition().ToString(),
            ["nativeEnabled"] = _appSettings.Native.Enabled,
            ["nativePollPeriodSeconds"] = _appSettings.Native.PollPeriod,
            ["nativeRetryTimeSeconds"] = _appSettings.Native.RetryTime,
            ["ghubEnabled"] = _appSettings.GHub.Enabled,
            ["httpEnabled"] = _appSettings.HTTPServer.Enabled,
            ["httpPort"] = _appSettings.HTTPServer.Port,
            ["language"] = Language,
            ["theme"] = ThemeMode,
            ["numericDisplay"] = NumericDisplay,
            ["autoCheckUpdates"] = AutoCheckUpdates,
            ["alertSummary"] = BuildSettingsDiagnosticsSummary(),
        };
    }

    private JsonObject BuildSettingsDiagnosticsSummary()
    {
        JsonObject root = new()
        {
            ["uiScaleMode"] = _settings.UiScaleMode,
            ["globalThresholdPercent"] = _settings.DefaultThresholdPercent,
            ["globalWindowsNotification"] = _settings.DefaultWindowsNotification,
            ["globalTrayBlink"] = _settings.DefaultTrayBlink,
            ["quietHoursEnabled"] = _settings.QuietHoursEnabled,
            ["suppressNotificationsWhenFullscreen"] = _settings.SuppressNotificationsWhenFullscreen,
            ["selectedDeviceCount"] = _settings.SelectedDevices.Count(),
            ["deviceSettingsCount"] = _settings.Snapshot.Devices.Count,
        };

        JsonArray deviceSettings = [];
        foreach (KeyValuePair<string, DeviceAlertSettings> pair in _settings.Snapshot.Devices.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            DeviceAlertSettings settings = pair.Value;
            deviceSettings.Add(new JsonObject
            {
                ["deviceIdHash"] = HashForDiagnostics(pair.Key),
                ["hasAlias"] = !string.IsNullOrWhiteSpace(settings.Alias),
                ["hasCustomThreshold"] = settings.ThresholdPercent.HasValue,
                ["hasCustomWindowsNotification"] = settings.WindowsNotification.HasValue,
                ["hasCustomTrayBlink"] = settings.TrayBlink.HasValue,
                ["hasCustomNumericDisplay"] = settings.NumericDisplay.HasValue,
                ["isPaused"] = settings.PauseUntil.HasValue,
                ["lastDeviceType"] = settings.LastDeviceType?.ToString(),
            });
        }

        root["deviceSettings"] = deviceSettings;
        return root;
    }

    private JsonArray BuildRecognizedDevicesJson()
    {
        JsonArray devices = [];
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            devices.Add(new JsonObject
            {
                ["deviceIdHash"] = HashForDiagnostics(device.DeviceId),
                ["deviceName"] = device.DeviceName,
                ["hasAlias"] = device.HasAlias,
                ["deviceType"] = device.DeviceType.ToString(),
                ["hasBattery"] = device.HasBattery,
                ["isOnline"] = device.IsOnline,
                ["batteryPercentage"] = device.BatteryPercentage,
                ["powerSupplyStatus"] = device.PowerSupplyStatus.ToString(),
                ["batteryVoltage"] = device.BatteryVoltage,
                ["lastUpdate"] = device.LastUpdate == DateTimeOffset.MinValue ? null : device.LastUpdate.ToString("o"),
            });
        }

        return devices;
    }

    private string BuildDiagnosticsSummary(NativeDiagnosticsResponseMessage? nativeResponse, string? nativeError)
    {
        StringBuilder sb = new();
        sb.AppendLine(Loc["DiagnosticsTitle"]);
        sb.AppendLine($"{Loc["ReleaseVersion"]}: {CurrentVersion}");
        sb.AppendLine($"{Loc["DiagnosticsGenerated"]}: {DateTimeOffset.Now:o}");
        sb.AppendLine($"{Loc["CurrentLanguage"]}: {Language}");
        sb.AppendLine($"{Loc["GHubStatus"]}: {GHubStatus}");
        sb.AppendLine($"{Loc["Port9010Status"]}: {Port9010Status}");
        sb.AppendLine(string.Format(Loc["NativeDiagnostics"], nativeResponse == null ? Loc["Unavailable"] : Loc["Available"]));
        if (!string.IsNullOrWhiteSpace(nativeError))
        {
            sb.AppendLine(string.Format(Loc["NativeDiagnosticsError"], nativeError));
        }
        if (!string.IsNullOrWhiteSpace(nativeResponse?.summaryText))
        {
            sb.AppendLine(nativeResponse.summaryText);
        }
        sb.AppendLine();
        sb.AppendLine($"{Loc["RecognizedDevices"]}:");
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            sb.AppendLine($"- {HashForDiagnostics(device.DeviceId)} | {device.DeviceName} | {device.DeviceType} | {device.BatteryPercentage:0.00}% | {device.PowerSupplyStatus}");
        }

        return sb.ToString();
    }

    private string BuildDiagnosticsReadme()
    {
        if (Loc.CurrentLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return """
PowerTray 诊断包

报告不支持的 Logitech 设备时，请附上 diagnostics.json。
此诊断包会对 HID 路径、设备标识、序列号和原始 HID++ identity 响应做哈希处理，
并且不会导出自定义别名明文。产品名称、产品 ID、usage page、接口编号、HID++ feature map 和电量探测结果会被保留，
因为这些信息用于判断和新增设备支持。

重要字段：
- hidEnumeration：Windows 当前可见的所有 Logitech HID 端点。
- unsupportedHidDevices：无法识别或无法打开的 Logitech HID 端点；不会列出其他厂商设备。
- nativeDiscovery：PowerTrayHID 在发现设备时尝试过的路径。
- nativeDiscovery[].failureReasons：设备或 session 被跳过的原因。
- nativeDiscovery[].devices[].identity：0x0003 identity 的 unit id、model id、serial 和原始响应哈希，以及最终 identifier 来源。
- nativeDiscovery[].devices[].featureMap：已识别设备暴露的 HID++ features。
- nativeDiscovery[].centurion：Centurion report id、device address、bridge 和电量数据。
- recognizedDevices：当前 UI 中显示的设备。
""";
        }

        if (Loc.CurrentLanguage.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
        {
            return """
PowerTray 診断パッケージ

未対応の Logitech デバイスを報告するときは diagnostics.json を添付してください。
このパッケージでは HID パス、デバイス識別子、シリアル番号、元の HID++ identity 応答をハッシュ化し、
カスタム表示名の平文は出力しません。製品名、製品 ID、usage page、インターフェイス番号、HID++ feature map、バッテリー検出結果は、
デバイス対応の判断に必要なため保持します。

主なフィールド:
- hidEnumeration: Windows から見えている Logitech HID エンドポイント。
- unsupportedHidDevices: 認識できなかった、または開けなかった Logitech HID エンドポイント。他社製デバイスは含みません。
- nativeDiscovery: PowerTrayHID が検出時に試した経路。
- nativeDiscovery[].failureReasons: デバイスまたは session がスキップされた理由。
- nativeDiscovery[].devices[].identity: 0x0003 identity の unit id、model id、serial、元応答のハッシュと最終 identifier の由来。
- nativeDiscovery[].devices[].featureMap: 認識済みデバイスが公開している HID++ features。
- nativeDiscovery[].centurion: Centurion report id、device address、bridge、バッテリー情報。
- recognizedDevices: 現在 UI に表示されているデバイス。
""";
        }

        return """
PowerTray diagnostics package

Please share diagnostics.json when reporting an unsupported Logitech device.
The package intentionally hashes HID paths, device identifiers, serial numbers,
and raw HID++ identity responses. Custom aliases are not exported in plain text.
Product names, product ids, usage pages, interface numbers, HID++ feature maps,
and battery probe results are kept because they are needed to add device support.

Important fields:
- hidEnumeration: all Logitech HID endpoints visible to Windows.
- unsupportedHidDevices: Logitech HID endpoints that were not recognized or could not be opened. Other vendors are excluded.
- nativeDiscovery: what PowerTrayHID tried during discovery.
- nativeDiscovery[].failureReasons: why a device/session was skipped.
- nativeDiscovery[].devices[].identity: unit id, model id, serial, and raw
  0x0003 identity response hashes plus the final identifier source.
- nativeDiscovery[].devices[].featureMap: HID++ features exposed by a recognized device.
- nativeDiscovery[].centurion: Centurion report id, device address, bridge, and battery data.
- recognizedDevices: devices currently shown by the UI.
""";
    }

    private static JsonNode CloneOrEmptyArray(JsonNode? node)
    {
        return node?.DeepClone() ?? new JsonArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream entryStream = entry.Open();
        using StreamWriter writer = new(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string HashForDiagnostics(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private static string GetCurrentVersion()
    {
        string? version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0];

        return string.IsNullOrWhiteSpace(version) ? "Missing" : version;
    }
}

public sealed record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

public sealed record UiScaleOption(int Index, string Code, double Scale, string LabelKey);

public sealed partial class DeviceSettingsItemViewModel : ObservableObject, IDisposable
{
    private readonly LogiDeviceViewModel _device;
    private readonly UserSettingsWrapper _settings;
    private readonly NotificationService _notifications;
    private readonly AlertManager _alertManager;
    private readonly Action<DeviceSettingsItemViewModel> _removeHistory;
    private readonly PropertyChangedEventHandler _devicePropertyChangedHandler;
    private int _thresholdDraft;
    private bool _isEditingThreshold;

    public LocalizationService Loc { get; }
    public string DeviceId => _device.DeviceId;
    public string OriginalName => _device.OriginalNameDisplay;
    public string DisplayName => _device.BaseDisplayName;
    public bool ShowOriginalName => _device.ShowOriginalName;
    public bool IsOnline => _device.IsOnline;
    public bool IsOffline => !_device.IsOnline;
    public bool ShowBatteryMetadata => _device.IsOnline && _device.BatteryPercentage >= 0;
    public string BatteryText => ShowBatteryMetadata ? $"{_device.BatteryPercentage:0}%" : "-";
    public string LastUpdateText => ShowBatteryMetadata && _device.LastUpdate != DateTimeOffset.MinValue ? _device.LastUpdate.ToString("g") : "-";
    public bool IsPaused => _settings.IsDevicePaused(DeviceId, DateTimeOffset.Now);
    public string PauseText
    {
        get
        {
            if (_settings.IsPausedUntilNextLaunch(DeviceId))
            {
                return Loc["PausedUntilNextLaunch"];
            }

            return _settings.GetPauseUntil(DeviceId) is { } pauseUntil && pauseUntil > DateTimeOffset.Now
                ? string.Format(Loc["PausedUntil"], pauseUntil.ToLocalTime().ToString("g"))
                : "-";
        }
    }
    public bool IsEditingThreshold => _isEditingThreshold;
    public bool FollowGlobalThreshold
    {
        get => !_settings.HasDeviceThreshold(DeviceId);
        set
        {
            if (FollowGlobalThreshold == value)
            {
                return;
            }

            if (value)
            {
                _settings.SetDeviceThreshold(DeviceId, null);
            }
            else
            {
                _settings.SetDeviceThreshold(DeviceId, _settings.DefaultThresholdPercent);
            }

            Refresh();
        }
    }

    public bool IsCustomThreshold => !FollowGlobalThreshold;

    public bool FollowGlobalNumericDisplay
    {
        get => !_settings.HasDeviceNumericDisplayOverride(DeviceId);
        set
        {
            if (FollowGlobalNumericDisplay == value)
            {
                return;
            }

            _settings.SetDeviceNumericDisplayOverride(DeviceId, value ? null : _settings.NumericDisplay);
            Refresh();
        }
    }

    public bool IsCustomNumericDisplay => !FollowGlobalNumericDisplay;

    public bool NumericDisplay
    {
        get => _settings.GetDeviceNumericDisplay(DeviceId);
        set
        {
            _settings.SetDeviceNumericDisplayOverride(DeviceId, value);
            Refresh();
        }
    }

    public DeviceSettingsItemViewModel(
        LogiDeviceViewModel device,
        UserSettingsWrapper settings,
        LocalizationService loc,
        NotificationService notifications,
        AlertManager alertManager,
        Action<DeviceSettingsItemViewModel> removeHistory
    )
    {
        _device = device;
        _settings = settings;
        Loc = loc;
        _notifications = notifications;
        _alertManager = alertManager;
        _removeHistory = removeHistory;
        _thresholdDraft = _settings.GetThreshold(DeviceId);
        _devicePropertyChangedHandler = (_, _) => Refresh();
        _device.PropertyChanged += _devicePropertyChangedHandler;
    }

    public void Dispose()
    {
        _device.PropertyChanged -= _devicePropertyChangedHandler;
    }

    public string Alias
    {
        get => _settings.GetDeviceSettings(DeviceId, OriginalName).Alias;
        set
        {
            _settings.SetDeviceAlias(DeviceId, value);
            Refresh();
        }
    }

    public int ThresholdPercent
    {
        get => _thresholdDraft;
        set
        {
            int threshold = Math.Clamp(value, 1, 100);
            if (_thresholdDraft == threshold)
            {
                return;
            }

            _thresholdDraft = threshold;
            _isEditingThreshold = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdPercentText));
        }
    }

    public void CommitThresholdPercent()
    {
        int threshold = Math.Clamp(_thresholdDraft, 1, 100);
        _isEditingThreshold = false;
        if (_settings.GetThreshold(DeviceId) != threshold)
        {
            _settings.SetDeviceThreshold(DeviceId, threshold);
        }

        _thresholdDraft = _settings.GetThreshold(DeviceId);
        OnPropertyChanged(nameof(ThresholdPercent));
        OnPropertyChanged(nameof(ThresholdPercentText));
        OnPropertyChanged(nameof(FollowGlobalThreshold));
        OnPropertyChanged(nameof(IsCustomThreshold));
    }

    private void SyncThresholdDraftFromSettings()
    {
        if (_isEditingThreshold)
        {
            return;
        }

        int threshold = _settings.GetThreshold(DeviceId);
        if (_thresholdDraft == threshold)
        {
            return;
        }

        _thresholdDraft = threshold;
        OnPropertyChanged(nameof(ThresholdPercent));
        OnPropertyChanged(nameof(ThresholdPercentText));
    }

    public string ThresholdPercentText => $"{ThresholdPercent}%";

    public bool WindowsNotification
    {
        get => _settings.GetWindowsNotificationEnabled(DeviceId);
        set
        {
            _settings.SetDeviceWindowsNotification(DeviceId, value);
            Refresh();
        }
    }

    public bool TrayBlink
    {
        get => _settings.GetTrayBlinkEnabled(DeviceId);
        set
        {
            _settings.SetDeviceTrayBlink(DeviceId, value);
            Refresh();
        }
    }

    [RelayCommand]
    private void TestNotification()
    {
        _notifications.ShowTest(_device);
    }

    [RelayCommand]
    private void TestBlink()
    {
        _alertManager.TestBlink(_device);
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _settings.RestoreDeviceDefaults(DeviceId);
        Refresh();
    }

    [RelayCommand]
    private void RemoveHistory()
    {
        string result = ThemedMessageBox.ShowOptions(
            string.Format(Loc["ConfirmForgetDeviceBody"], DisplayName),
            Loc["ConfirmForgetDeviceTitle"],
            [
                new(Loc["Cancel"], "cancel", IsDefault: true, IsCancel: true),
                new(Loc["RemoveHistory"], "forget", IsDestructive: true),
            ]);

        if (result != "forget")
        {
            return;
        }

        _removeHistory(this);
    }

    [RelayCommand]
    private void PauseOneHour()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset baseTime = _settings.GetPauseUntil(DeviceId) is { } pauseUntil && pauseUntil > now
            ? pauseUntil
            : now;

        _settings.SetDevicePauseUntil(DeviceId, baseTime.AddHours(1));
        Refresh();
    }

    [RelayCommand]
    private void PauseUntilNextLaunch()
    {
        _settings.SetDevicePauseUntilNextLaunch(DeviceId);
        Refresh();
    }

    [RelayCommand]
    private void ResumeAlerts()
    {
        _settings.SetDevicePauseUntil(DeviceId, null);
        Refresh();
    }

    public void Refresh()
    {
        SyncThresholdDraftFromSettings();
        OnPropertyChanged(string.Empty);
    }
}
