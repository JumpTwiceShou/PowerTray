using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LGSTrayCore;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Extensions.Options;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;

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
    private readonly AppSettings _appSettings;
    private bool _lastGHubRunning;
    private bool _lastPort9010Reachable;

    public LocalizationService Loc { get; }
    public ObservableCollection<DeviceSettingsItemViewModel> DeviceItems { get; } = [];

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
        _appSettings = appSettings.Value;
        Loc = loc;

        _deviceCollection.Devices.CollectionChanged += OnDevicesChanged;
        _settings.DeviceSettingsChanged += OnDeviceSettingsChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        Loc.PropertyChanged += (_, _) => RefreshBindings();
        RefreshDeviceItems();
        _ = RefreshDiagnosticsAsync();
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
        get => _settings.DefaultThresholdPercent;
        set
        {
            if (_settings.DefaultThresholdPercent == value)
            {
                return;
            }

            _settings.DefaultThresholdPercent = value;
            RefreshBindings();
        }
    }

    public string DefaultThresholdPercentText => $"{DefaultThresholdPercent}%";

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
        string? nativeError = nativeResponse == null ? "PowerTrayHID did not respond before the diagnostics timeout." : nativeResponse.error;
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
        RefreshStatusText();
        OnPropertyChanged(string.Empty);
        foreach (DeviceSettingsItemViewModel item in DeviceItems)
        {
            item.Refresh();
        }
        DiagnosticSummary = BuildDiagnostics();
    }

    private void RefreshStatusText()
    {
        GHubStatus = _lastGHubRunning ? Loc["Yes"] : Loc["No"];
        Port9010Status = _lastPort9010Reachable ? Loc["Yes"] : Loc["No"];
    }

    private string BuildDiagnostics()
    {
        StringBuilder sb = new();
        sb.AppendLine("PowerTray Diagnostics");
        sb.AppendLine($"{Loc["ReleaseVersion"]}: {CurrentVersion}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:o}");
        sb.AppendLine($"{Loc["CurrentLanguage"]}: {Language}");
        sb.AppendLine($"{Loc["Theme"]}: {ThemeMode}");
        sb.AppendLine($"{Loc["GHubStatus"]}: {GHubStatus}");
        sb.AppendLine($"{Loc["Port9010Status"]}: {Port9010Status}");
        sb.AppendLine();
        sb.AppendLine(Loc["AlertSummary"]);
        sb.AppendLine(_settings.ExportSettingsSummary());
        sb.AppendLine();
        sb.AppendLine("Devices:");
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            sb.AppendLine($"{device.DeviceId} | {device.DeviceName} | {device.DeviceType} | {device.BatteryPercentage:0.00}% | {device.PowerSupplyStatus} | {device.LastUpdate:o}");
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
                nativeError = "PowerTrayHID returned diagnostics JSON that could not be parsed.";
            }
        }

        JsonObject root = new()
        {
            ["schemaVersion"] = 1,
            ["generatedAt"] = DateTimeOffset.Now.ToString("o"),
            ["appVersion"] = CurrentVersion,
            ["system"] = BuildSystemJson(),
            ["appSettings"] = BuildAppSettingsJson(),
            ["hidEnumeration"] = CloneOrEmptyArray(nativeNode?["hidEnumeration"]),
            ["unsupportedHidDevices"] = CloneOrEmptyArray(nativeNode?["unsupportedHidDevices"]),
            ["nativeDiscovery"] = CloneOrEmptyArray(nativeNode?["nativeDiscovery"]),
            ["recognizedDevices"] = BuildRecognizedDevicesJson(),
            ["recentEvents"] = CloneOrEmptyArray(nativeNode?["recentEvents"]),
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
            ["alertSummary"] = _settings.ExportSettingsSummary(),
        };
    }

    private JsonArray BuildRecognizedDevicesJson()
    {
        JsonArray devices = [];
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            devices.Add(new JsonObject
            {
                ["deviceId"] = device.DeviceId,
                ["deviceName"] = device.DeviceName,
                ["displayName"] = device.BaseDisplayName,
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
        sb.AppendLine("PowerTray Diagnostics");
        sb.AppendLine($"{Loc["ReleaseVersion"]}: {CurrentVersion}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:o}");
        sb.AppendLine($"{Loc["CurrentLanguage"]}: {Language}");
        sb.AppendLine($"{Loc["GHubStatus"]}: {GHubStatus}");
        sb.AppendLine($"{Loc["Port9010Status"]}: {Port9010Status}");
        sb.AppendLine($"Native diagnostics: {(nativeResponse == null ? "unavailable" : "available")}");
        if (!string.IsNullOrWhiteSpace(nativeError))
        {
            sb.AppendLine($"Native diagnostics error: {nativeError}");
        }
        if (!string.IsNullOrWhiteSpace(nativeResponse?.summaryText))
        {
            sb.AppendLine(nativeResponse.summaryText);
        }
        sb.AppendLine();
        sb.AppendLine("Recognized devices:");
        foreach (LogiDeviceViewModel device in _deviceCollection.Devices)
        {
            sb.AppendLine($"- {device.DeviceId} | {device.DeviceName} | {device.DeviceType} | {device.BatteryPercentage:0.00}% | {device.PowerSupplyStatus}");
        }

        return sb.ToString();
    }

    private static string BuildDiagnosticsReadme()
    {
        return """
PowerTray diagnostics package

Please share diagnostics.json when reporting an unsupported Logitech device.
The package intentionally hashes HID paths and serial numbers. Product names,
product ids, usage pages, interface numbers, HID++ feature maps, and battery
probe results are kept because they are needed to add device support.

Important fields:
- hidEnumeration: all Logitech HID endpoints visible to Windows.
- unsupportedHidDevices: non-Logitech HID background endpoints and Logitech
  endpoints that were not probed or could not be opened.
- nativeDiscovery: what PowerTrayHID tried during discovery.
- nativeDiscovery[].failureReasons: why a device/session was skipped.
- nativeDiscovery[].devices[].identity: raw 0x0003 identity responses, unit id,
  model id, serial response, and the final identifier source.
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

    private static string GetCurrentVersion()
    {
        string? version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0];

        return string.IsNullOrWhiteSpace(version) ? "Missing" : version;
    }
}

public sealed partial class DeviceSettingsItemViewModel : ObservableObject
{
    private readonly LogiDeviceViewModel _device;
    private readonly UserSettingsWrapper _settings;
    private readonly NotificationService _notifications;
    private readonly AlertManager _alertManager;
    private readonly Action<DeviceSettingsItemViewModel> _removeHistory;
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
        _device.PropertyChanged += (_, _) => Refresh();
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
        get => _settings.GetThreshold(DeviceId);
        set
        {
            int threshold = Math.Clamp(value, 1, 100);
            if (ThresholdPercent == threshold)
            {
                return;
            }

            _isEditingThreshold = true;
            try
            {
                _settings.SetDeviceThreshold(DeviceId, threshold);
            }
            finally
            {
                _isEditingThreshold = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdPercentText));
            OnPropertyChanged(nameof(FollowGlobalThreshold));
            OnPropertyChanged(nameof(IsCustomThreshold));
        }
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
        OnPropertyChanged(string.Empty);
    }
}
