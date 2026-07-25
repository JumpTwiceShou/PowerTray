using Hardcodet.Wpf.TaskbarNotification;
using LGSTrayCore;
using LGSTrayHID;
using LGSTrayHID.Features;
using LGSTrayHID.HidApi;
using LGSTrayUI;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayPrimitives.IPC;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void TestXmlEscaping()
{
    LogiDevice device = new()
    {
        DeviceId = "id&1",
        DeviceName = "Logi <Mouse> & \"Test\"",
        DeviceType = DeviceType.Mouse,
        BatteryPercentage = 86,
        BatteryVoltage = 4.2,
        BatteryMileage = -1,
        PowerSupplyStatus = PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING,
    };

    string xml = device.GetXmlData();

    Assert(xml.Contains("<device_id>id&amp;1</device_id>"), "Device id should be XML escaped.");
    Assert(xml.Contains("<device_name>Logi &lt;Mouse&gt; &amp; &quot;Test&quot;</device_name>"), "Device name should be XML escaped.");
    Assert(xml.Contains("<battery_percent>86.00</battery_percent>"), "Battery percentage should use invariant decimal formatting.");
}

static void TestLastUpdateDoesNotWriteDeviceMetadataToConsole()
{
    LogiDevice device = new()
    {
        DeviceName = "Private device name",
        BatteryPercentage = 42,
    };
    TextWriter originalOutput = Console.Out;
    using StringWriter capturedOutput = new();
    try
    {
        Console.SetOut(capturedOutput);
        device.LastUpdate = DateTimeOffset.UtcNow;
    }
    finally
    {
        Console.SetOut(originalOutput);
    }

    Assert(capturedOutput.ToString().Length == 0, "Updating a device must not write its name or battery state to standard output.");
}

static void TestBattery1F20Decode()
{
    var decoded = Battery1F20.Decode(0x10, 0x5A, 0x01)
        ?? throw new InvalidOperationException("Valid 1F20 ADC payload should decode.");

    Assert(decoded.batteryPercentage == 100, "4186 mV should decode to 100%.");
    Assert(decoded.status == PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, "Unset charging flag should be discharging.");
    Assert(Battery1F20.Decode(0x10, 0x5A, 0x00) == null, "Invalid 1F20 ADC payload should return null.");
}

static void TestBattery1001LookupBoundaries()
{
    Assert(Battery1001.LookupBatPercent(4186) == 100, "4186 mV should decode to 100% for Battery1001.");
    Assert(Battery1001.LookupBatPercent(3537) == 1, "The lowest Battery1001 LUT threshold should decode to 1%.");
    Assert(Battery1001.LookupBatPercent(3536) == 0, "Below the lowest Battery1001 LUT threshold should decode to 0%.");
}

static void TestHidDeviceInfoX64AbiLayout()
{
    Assert(Environment.Is64BitProcess, "Native HID ABI validation must run as x64.");
    Assert(Marshal.SizeOf<HidDeviceInfo>() == 72, "hid_device_info must remain 72 bytes on Windows x64.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("Path").ToInt32() == 0, "hid_device_info.path offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("VendorId").ToInt32() == 8, "hid_device_info.vendor_id offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("ProductId").ToInt32() == 10, "hid_device_info.product_id offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("SerialNumber").ToInt32() == 16, "hid_device_info.serial_number offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("ReleaseNumber").ToInt32() == 24, "hid_device_info.release_number offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("ManufacturerString").ToInt32() == 32, "hid_device_info.manufacturer_string offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("ProductString").ToInt32() == 40, "hid_device_info.product_string offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("UsagePage").ToInt32() == 48, "hid_device_info.usage_page offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("Usage").ToInt32() == 50, "hid_device_info.usage offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("InterfaceNumber").ToInt32() == 52, "hid_device_info.interface_number offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("Next").ToInt32() == 56, "hid_device_info.next offset mismatch.");
    Assert(Marshal.OffsetOf<HidDeviceInfo>("BusType").ToInt32() == 64, "hid_device_info.bus_type offset mismatch.");
}

static void TestNativeIdentityDiagnosticsRedaction()
{
    byte[] deviceInfoRaw = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77];
    byte[] deviceInfoParams =
    [
        0x00,
        0xDE, 0xAD, 0xBE, 0xEF,
        0x00, 0x00,
        0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
        0x00,
        0x01
    ];
    byte[] serialRaw = [0x20, 0x21, 0x22, 0x23];
    byte[] serialParams = [0x12, 0x34, 0x56, 0x78];

    var identity = HidppDeviceIdentity.FromDeviceInformation(
        "Test Device",
        0x0AAA,
        0x01,
        3,
        "receiver-stable-id",
        "endpoint-alias",
        deviceInfoRaw,
        deviceInfoParams,
        serialRaw,
        serialParams
    );

    string json = JsonSerializer.Serialize(identity.ToDiagnostic());

    Assert(!json.Contains("12345678", StringComparison.OrdinalIgnoreCase), "Diagnostics should not include raw serial numbers.");
    Assert(!json.Contains("11223344556677", StringComparison.OrdinalIgnoreCase), "Diagnostics should not include raw device info responses.");
    Assert(!json.Contains("20212223", StringComparison.OrdinalIgnoreCase), "Diagnostics should not include raw serial responses.");
    Assert(json.Contains("SerialNumberHash", StringComparison.Ordinal), "Diagnostics should include a serial hash field.");
}

static void TestUpdaterAssetSelectionAndChecksum()
{
    string[] assets =
    [
        "PowerTraySetup.exe",
        "PowerTraySetup.exe.sha256",
        "PowerTraySetup-full.exe",
        "PowerTraySetup-full.exe.sha256",
        "PowerTray-diagnostics.exe",
        "other.exe",
    ];

    Assert(UpdateService.SelectInstallerAssetName(assets, InstallerEdition.Light) == "PowerTraySetup.exe", "Light updater should select the strict light installer.");
    Assert(UpdateService.SelectInstallerAssetName(assets, InstallerEdition.Full) == "PowerTraySetup-full.exe", "Full updater should select the strict full installer.");
    Assert(UpdateService.SelectInstallerAssetName(["other.exe"], InstallerEdition.Light) == null, "Updater should not fall back to arbitrary exe assets.");

    string hash = new('a', 64);
    Assert(UpdateService.TryParseSha256Checksum($"{hash}  PowerTraySetup.exe", "PowerTraySetup.exe", out string parsedHash), "Checksum parser should accept matching sha256 files.");
    Assert(parsedHash == hash, "Checksum parser should return the expected hash.");
    Assert(!UpdateService.TryParseSha256Checksum($"{hash}  other.exe", "PowerTraySetup.exe", out _), "Checksum parser should reject mismatched filenames.");
}

static void TestHttpServerLoopbackFallback()
{
    HttpServerSettings settings = new()
    {
        Port = 12321,
        Addr = "0.0.0.0",
        AllowRemote = false,
    };

    Assert(settings.UrlPrefix == "http://localhost:12321", "HTTP server should fall back to loopback unless remote binding is explicit.");

    settings.AllowRemote = true;
    settings.AccessToken = new string('x', 32);
    Assert(settings.UrlPrefix == "http://localhost:12321", "Legacy remote settings must remain loopback-only.");
    Assert(!settings.IsRemoteAccessConfigured, "PowerTray 1.5.0 must not expose a remote HTTP mode.");
    Assert(!settings.RequiresAuthentication, "The loopback-only HTTP API does not require remote authentication.");
    Assert(settings.IsAuthorized(null), "Loopback requests should remain available without a token.");

    settings.Addr = "2001:db8::1";
    Assert(settings.UrlPrefix == "http://localhost:12321", "Non-loopback IPv6 settings must fall back to localhost.");

    settings.Addr = "::1";
    Assert(settings.UrlPrefix == "http://[::1]:12321", "Explicit IPv6 loopback should remain supported.");
}

static void TestHidHotplugRegistrationPolicy()
{
    Assert(HidHotplugRegistrationPolicy.IsAvailable(0, 1), "A successful hotplug registration with a valid handle should be available.");
    Assert(!HidHotplugRegistrationPolicy.IsAvailable(1, 1), "A failed hotplug registration must activate the rediscovery fallback.");
    Assert(!HidHotplugRegistrationPolicy.IsAvailable(0, 0), "A missing callback handle must activate the rediscovery fallback.");
}

static void TestTrayToolTipSeparators()
{
    Assert(LogiDeviceViewModel.FormatToolTipDetail("测试", "39.00%") == "测试，39.00%", "CJK tooltip separator should be a full-width comma without a space.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("G502", "39.00%") == "G502, 39.00%", "ASCII tooltip separator should be a comma plus a space.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("マウス", "39.00%") == "マウス，39.00%", "Japanese tooltip separator should be a full-width comma.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("Ｇ５０２", "39.00%") == "Ｇ５０２，39.00%", "Full-width tooltip separator should be a full-width comma.");

    Version hardcodetVersion = typeof(TaskbarIcon).Assembly.GetName().Version
        ?? throw new InvalidOperationException("Hardcodet assembly version should be available.");
    Assert(hardcodetVersion >= new Version(2, 0, 0, 0), "The themed tray tooltip candidate requires Hardcodet 2.x.");
}

static void TestTrayToolTipModesAndSettingsMigration()
{
    Assert(TrayToolTipModePolicy.Parse(null) == TrayToolTipMode.PowerTrayCustom, "A missing tooltip mode must preserve the existing custom-tooltip behavior.");
    Assert(TrayToolTipModePolicy.Parse("invalid") == TrayToolTipMode.PowerTrayCustom, "An invalid tooltip mode must safely fall back to PowerTrayCustom.");
    Assert(TrayToolTipModePolicy.Serialize((TrayToolTipMode)999) == nameof(TrayToolTipMode.PowerTrayCustom), "An undefined tooltip mode must serialize to the safe default.");

    PowerTrayUserSettings missingField = JsonSerializer.Deserialize<PowerTrayUserSettings>("{}")
        ?? throw new InvalidOperationException("Missing-field settings fixture should deserialize.");
    UserSettingsWrapper.NormalizeSettingsForTesting(missingField);
    Assert(missingField.SchemaVersion == 2, "Old settings should migrate to schema version 2.");
    Assert(missingField.TrayToolTipMode == nameof(TrayToolTipMode.PowerTrayCustom), "Old settings without a tooltip field should retain custom hover.");

    PowerTrayUserSettings invalid = new() { SchemaVersion = 1, TrayToolTipMode = "not-a-mode" };
    UserSettingsWrapper.NormalizeSettingsForTesting(invalid);
    Assert(invalid.TrayToolTipMode == nameof(TrayToolTipMode.PowerTrayCustom), "Invalid persisted tooltip values should normalize to PowerTrayCustom.");

    foreach (TrayToolTipMode mode in Enum.GetValues<TrayToolTipMode>())
    {
        PowerTrayUserSettings legal = new() { TrayToolTipMode = TrayToolTipModePolicy.Serialize(mode) };
        UserSettingsWrapper.NormalizeSettingsForTesting(legal);
        Assert(TrayToolTipModePolicy.Parse(legal.TrayToolTipMode) == mode, $"The legal tooltip mode {mode} should round-trip through settings normalization.");

        TrayToolTipRegistration registration = TrayToolTipRegistration.For(mode);
        int implementations = (registration.UsesNativeText ? 1 : 0) + (registration.UsesCustomContent ? 1 : 0);
        Assert(implementations == (mode == TrayToolTipMode.Disabled ? 0 : 1), $"Tooltip mode {mode} must activate exactly one hover implementation.");
        Assert(registration.SubscribesCustomOpenEvent == registration.UsesCustomContent, "Only the custom WPF tooltip may subscribe to the custom-open event.");
    }

    TrayToolTipModeChangeDecision newPending = TrayToolTipModeChangePolicy.Evaluate(
        TrayToolTipMode.PowerTrayCustom,
        TrayToolTipMode.PowerTrayCustom,
        TrayToolTipMode.WindowsNative
    );
    Assert(newPending is { ShouldSave: true, ShouldPromptForRestart: true }, "A real user change away from the effective mode should save and prompt once.");

    TrayToolTipModeChangeDecision repeatedPending = TrayToolTipModeChangePolicy.Evaluate(
        TrayToolTipMode.PowerTrayCustom,
        TrayToolTipMode.WindowsNative,
        TrayToolTipMode.WindowsNative
    );
    Assert(repeatedPending is { ShouldSave: false, ShouldPromptForRestart: false }, "Selecting the same pending value must not prompt repeatedly.");

    TrayToolTipModeChangeDecision cancelPending = TrayToolTipModeChangePolicy.Evaluate(
        TrayToolTipMode.PowerTrayCustom,
        TrayToolTipMode.WindowsNative,
        TrayToolTipMode.PowerTrayCustom
    );
    Assert(cancelPending is { ShouldSave: true, ShouldPromptForRestart: false }, "Re-selecting the effective mode should clear pending restart state without another prompt.");
}

static void TestNativeTrayToolTipLength()
{
    Assert(NativeTrayToolTipText.Limit(new string('a', 127)).Length == 127, "Native tooltip text at the Windows limit should remain unchanged.");
    Assert(NativeTrayToolTipText.Limit(new string('a', 128)).Length == 127, "Native tooltip text must reserve one UTF-16 code unit for the null terminator.");
    Assert(NativeTrayToolTipText.Limit(new string('测', 200)).Length == 127, "CJK native tooltip text should be limited by UTF-16 code units.");

    string surrogateBoundary = new string('a', 126) + "😀" + "z";
    string truncated = NativeTrayToolTipText.Limit(surrogateBoundary);
    Assert(truncated.Length == 126, "Native tooltip truncation must back up before a split surrogate pair.");
    Assert(!truncated.Any(char.IsSurrogate), "Native tooltip truncation must not leave an unpaired surrogate.");

    string longCustomText = new string('x', 512);
    TrayToolTipRegistration custom = TrayToolTipRegistration.For(TrayToolTipMode.PowerTrayCustom);
    Assert(custom.UsesCustomContent && !custom.UsesNativeText && longCustomText.Length == 512, "Custom tooltip mode must not apply the native 127-code-unit limit.");
}

static void TestLocalizationCatalogs()
{
    IReadOnlyDictionary<string, string> zh = LocalizationService.GetCatalogForTesting("zh-CN");
    IReadOnlyDictionary<string, string> en = LocalizationService.GetCatalogForTesting("en-US");
    IReadOnlyDictionary<string, string> ja = LocalizationService.GetCatalogForTesting("ja-JP");

    string[] zhKeys = zh.Keys.Order(StringComparer.Ordinal).ToArray();
    Assert(zhKeys.SequenceEqual(en.Keys.Order(StringComparer.Ordinal)), "English localization keys must exactly match the Chinese semantic source.");
    Assert(zhKeys.SequenceEqual(ja.Keys.Order(StringComparer.Ordinal)), "Japanese localization keys must exactly match the Chinese semantic source.");
    Assert(!zhKeys.Any(key => key.StartsWith("BatteryStatus", StringComparison.Ordinal)), "Tray tooltip power-status localization must not remain after the status text is removed.");

    foreach (string key in zhKeys)
    {
        Assert(!string.IsNullOrWhiteSpace(zh[key]) && !string.IsNullOrWhiteSpace(en[key]) && !string.IsNullOrWhiteSpace(ja[key]), $"Localization key {key} must be non-empty in all languages.");
        string ChinesePlaceholders = string.Join("|", Regex.Matches(zh[key], @"\{\d+(?:[^{}]*)\}").Select(match => match.Value));
        string EnglishPlaceholders = string.Join("|", Regex.Matches(en[key], @"\{\d+(?:[^{}]*)\}").Select(match => match.Value));
        string JapanesePlaceholders = string.Join("|", Regex.Matches(ja[key], @"\{\d+(?:[^{}]*)\}").Select(match => match.Value));
        Assert(ChinesePlaceholders == EnglishPlaceholders, $"English placeholders for {key} must match Chinese in order and format.");
        Assert(ChinesePlaceholders == JapanesePlaceholders, $"Japanese placeholders for {key} must match Chinese in order and format.");
    }

    Assert(ja["Alias"] == "カスタムデバイス名", "Japanese Alias must preserve the Chinese meaning of a custom device name.");
    Assert(ja["Port9010Status"].Contains("接続可能", StringComparison.Ordinal), "Japanese G Hub status must preserve the Chinese reachable-state meaning.");
    Assert(ja["ConfirmForgetDeviceBody"].EndsWith("続行しますか？", StringComparison.Ordinal), "Japanese device-removal text must preserve the Chinese confirmation question.");
    Assert(
        zh["TrayToolTipDescription"].Contains("Windows 11", StringComparison.Ordinal) &&
        zh["TrayToolTipDescription"].Contains("屏幕左上角", StringComparison.Ordinal) &&
        zh["TrayToolTipDescription"].Contains("Windows 原生悬浮或关闭悬浮", StringComparison.Ordinal),
        "The Simplified Chinese hover note must state the Windows 11 top-left risk and both avoidance choices."
    );
    Assert(
        en["TrayToolTipDescription"].Contains("top-left", StringComparison.OrdinalIgnoreCase) &&
        en["TrayToolTipDescription"].Contains("disable hover", StringComparison.OrdinalIgnoreCase),
        "The English hover note must preserve the top-left warning and disable option."
    );
    Assert(
        ja["TrayToolTipDescription"].Contains("画面左上", StringComparison.Ordinal) &&
        ja["TrayToolTipDescription"].Contains("無効", StringComparison.Ordinal),
        "The Japanese hover note must preserve the top-left warning and disable option."
    );

    string tempDirectory = Path.Combine(Path.GetTempPath(), $"PowerTray-bootstrap-loc-{Guid.NewGuid():N}");
    try
    {
        PowerTrayConstants.UserDataDirectoryOverrideForTests = tempDirectory;
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(PowerTrayConstants.SettingsPath, "{\"Language\":\"ja-JP\"}");
        Assert(LocalizationService.TranslateBootstrap("SettingsLoadErrorTitle") == ja["SettingsLoadErrorTitle"], "Startup settings errors should use the persisted language before dependency injection is available.");
        File.WriteAllText(PowerTrayConstants.SettingsPath, "{not-json");
        Assert(!string.IsNullOrWhiteSpace(LocalizationService.TranslateBootstrap("SettingsLoadErrorTitle")), "Malformed user settings must not prevent bootstrap error localization.");
    }
    finally
    {
        PowerTrayConstants.UserDataDirectoryOverrideForTests = null;
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

static async Task TestBatteryPollingLoopRecoversAfterUnexpectedFailureAsync()
{
    using CancellationTokenSource cancellation = new();
    int updateAttempts = 0;
    int recordedErrors = 0;

    await BatteryPollingLoop.RunAsync(
        _ => Task.CompletedTask,
        _ =>
        {
            updateAttempts++;
            if (updateAttempts == 1)
            {
                throw new InvalidDataException("transient parser failure");
            }

            cancellation.Cancel();
            return Task.CompletedTask;
        },
        TimeSpan.Zero,
        exception =>
        {
            Assert(exception is InvalidDataException, "The battery poll loop should report the original unexpected exception.");
            recordedErrors++;
        },
        cancellation.Token
    );

    Assert(updateAttempts == 2, "A single unexpected battery polling failure must not terminate all future polling for the device.");
    Assert(recordedErrors == 1, "The unexpected battery polling failure should be recorded exactly once.");
}

static void TestMessagePipeDiagnosticsPolicy()
{
#if DEBUG
    Assert(MessagePipeDiagnosticsPolicy.CaptureStackTrace, "Debug builds should retain MessagePipe subscription stack traces for diagnostics.");
#else
    Assert(!MessagePipeDiagnosticsPolicy.CaptureStackTrace, "Release builds must disable MessagePipe subscription stack traces to avoid production overhead.");
#endif
}

static void TestRestartWaitArgumentParsing()
{
    Assert(RestartWaitArguments.TryExtract(["--settings"], out RestartWaitTarget? noTarget, out string[] ordinaryArgs), "Ordinary startup arguments should parse.");
    Assert(noTarget == null && ordinaryArgs.SequenceEqual(["--settings"]), "Ordinary startup arguments should remain unchanged.");

    string[] restartArgs =
    [
        RestartWaitArguments.WaitForExitArgument,
        "123",
        RestartWaitArguments.WaitStartTimeArgument,
        "456",
        "--settings",
    ];
    Assert(RestartWaitArguments.TryExtract(restartArgs, out RestartWaitTarget? target, out string[] remaining), "A complete internal restart argument pair should parse.");
    Assert(target == new RestartWaitTarget(123, 456) && remaining.SequenceEqual(["--settings"]), "Internal wait arguments should be removed before normal startup processing.");
    Assert(!RestartWaitArguments.TryExtract([RestartWaitArguments.WaitForExitArgument, "123"], out _, out _), "A partial internal restart argument set must be rejected.");
    Assert(!RestartWaitArguments.TryExtract([RestartWaitArguments.WaitForExitArgument, "-1", RestartWaitArguments.WaitStartTimeArgument, "1"], out _, out _), "Invalid restart PIDs must be rejected.");

    using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
    string executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The test executable path should be available.");
    System.Diagnostics.ProcessStartInfo startInfo = RestartWaitArguments.CreateStartInfo(executablePath, current, reopenSettings: true);
    Assert(!startInfo.UseShellExecute, "Safe restart must use UseShellExecute=false.");
    Assert(Path.IsPathFullyQualified(startInfo.FileName), "Safe restart must use the absolute current executable path.");
    Assert(startInfo.ArgumentList.Contains(RestartWaitArguments.WaitForExitArgument) &&
           startInfo.ArgumentList.Contains(RestartWaitArguments.WaitStartTimeArgument) &&
           startInfo.ArgumentList.Contains("--settings"), "Safe restart must pass the PID, process start time, and settings reopen flag as separate arguments.");
}

static void TestRestartWaitProcessHandle()
{
    string executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The test executable path should be available.");
    System.Diagnostics.ProcessStartInfo startInfo = new(executablePath)
    {
        UseShellExecute = false,
        WorkingDirectory = AppContext.BaseDirectory,
    };
    startInfo.ArgumentList.Add("--restart-wait-child");

    using System.Diagnostics.Process child = System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("The restart wait fixture process should start.");
    long childStartTime = child.StartTime.ToUniversalTime().Ticks;
    Assert(RestartWaitArguments.WaitForPriorInstance(new RestartWaitTarget(child.Id, childStartTime + 1), TimeSpan.FromMilliseconds(100)), "A mismatched process start time should be treated as PID reuse, not as the old PowerTray instance.");

    RestartWaitTarget target = new(child.Id, childStartTime);
    Assert(RestartWaitArguments.WaitForPriorInstance(target, TimeSpan.FromSeconds(5)), "The replacement process should wait on the exact old process handle until it exits.");
    Assert(child.HasExited, "The wait helper should return only after the target process exits.");
}

static void TestTrayToolTipDisposalLifecycle()
{
    Exception? failure = null;
    Thread thread = new(() =>
    {
        try
        {
            object content = new();
            object dataContext = new();
            ToolTip toolTip = new()
            {
                Content = content,
                DataContext = dataContext,
            };
            bool detached = false;

            TrayToolTipLifecycle.CloseAndDetach(toolTip, () =>
            {
                Assert(!toolTip.IsOpen, "A device tooltip must be closed before it is detached from its tray icon.");
                Assert(toolTip.Content == null, "A disposed tray icon must not leave tooltip content attached to PopupRoot.");
                Assert(toolTip.DataContext == null, "A disposed tray icon must not leave the device data context attached to its tooltip.");
                detached = true;
            });

            Assert(detached, "Tooltip disposal must detach the tooltip from the TaskbarIcon.");
            bool nullDetached = false;
            TrayToolTipLifecycle.CloseAndDetach(null, () => nullDetached = true);
            Assert(nullDetached, "Tooltip disposal must still detach the TaskbarIcon when no resolved tooltip exists.");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure != null)
    {
        throw new InvalidOperationException("Tray tooltip disposal lifecycle failed.", failure);
    }
}

static void TestProductionTrayToolTipModeLifecycle()
{
    string executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The test executable path should be available.");
    System.Diagnostics.ProcessStartInfo startInfo = new(executablePath)
    {
        UseShellExecute = false,
        WorkingDirectory = AppContext.BaseDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("--tooltip-lifecycle-child");

    using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("The isolated WPF tooltip lifecycle process should start.");
    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
    Task<string> standardError = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30_000))
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("The isolated WPF tooltip lifecycle process timed out.");
    }

    string output = standardOutput.GetAwaiter().GetResult();
    string error = standardError.GetAwaiter().GetResult();
    Assert(process.ExitCode == 0, $"The isolated WPF tooltip lifecycle process failed. Output: {output} Error: {error}");
}

static void TestProductionTrayToolTipModeLifecycleCore()
{
    Exception? failure = null;
    Thread thread = new(() =>
    {
        Application? application = null;
        try
        {
            application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/PowerTray;component/NotifyIconResources.xaml", UriKind.Relative),
            });
            ThemeService.ApplyCurrentResources();
            double ordinaryComboWidth = (double)application.Resources["UISettingsComboWidth"];
            double trayToolTipComboWidth = (double)application.Resources["UITrayToolTipModeComboWidth"];
            Assert(
                Math.Abs(
                    trayToolTipComboWidth -
                    ordinaryComboWidth -
                    (2.0 * 14.0 * ThemeService.CurrentScale)
                ) < 0.001,
                "The tray-hover mode selector must be exactly two base CJK glyph widths wider than ordinary settings selectors."
            );

            foreach (TrayToolTipMode mode in Enum.GetValues<TrayToolTipMode>())
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), $"PowerTray-tooltip-mode-{mode}-{Guid.NewGuid():N}");
                try
                {
                    PowerTrayConstants.UserDataDirectoryOverrideForTests = tempDirectory;
                    Directory.CreateDirectory(tempDirectory);
                    PowerTrayUserSettings persisted = new()
                    {
                        Language = "zh-CN",
                        TrayToolTipMode = TrayToolTipModePolicy.Serialize(mode),
                    };
                    File.WriteAllText(
                        PowerTrayConstants.SettingsPath,
                        JsonSerializer.Serialize(persisted)
                    );

                    UserSettingsWrapper settings = new();
                    Assert(settings.EffectiveTrayToolTipMode == mode, $"The icon factory should freeze {mode} as the process-effective mode.");
                    AlertStateService alertState = new();
                    LogiDeviceIconFactory iconFactory = new(
                        Microsoft.Extensions.Options.Options.Create(new AppSettings()),
                        settings,
                        alertState
                    );
                    LocalizationService localization = new(settings);
                    using LogiDeviceViewModel device = new(iconFactory, settings, localization);
                    device.UpdateState(new InitMessage($"test-{mode}", $"测试设备 {mode} 😀", true, DeviceType.Mouse));
                    device.UpdateState(new UpdateMessage(
                        device.DeviceId,
                        73,
                        PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING,
                        4010,
                        DateTimeOffset.UtcNow
                    ));
                    Assert(device.DisplayToolTipString.Contains("73", StringComparison.Ordinal), "Tray hover should retain the battery percentage.");
                    Assert(device.DisplayToolTipString.Contains("4.01 V", StringComparison.Ordinal), "Tray hover should retain the optional battery voltage.");
                    Assert(!device.DisplayToolTipString.Contains("充电中", StringComparison.Ordinal), "Tray hover must not add a localized charging state.");
                    Assert(!device.DisplayToolTipString.Contains(" · ", StringComparison.Ordinal), "Tray hover must not add status separators.");
                    device.IsChecked = true;

                    LogiDeviceIcon firstIcon = device.TaskbarIconForTesting
                        ?? throw new InvalidOperationException($"Mode {mode} should create a tray icon through the production ViewModel path.");
                    TaskbarIcon taskbarIcon = firstIcon.TaskbarIconForTesting;
                    TrayToolTipRegistration registration = firstIcon.ToolTipRegistrationForTesting;
                    ToolTip? resolvedCustomToolTip = null;

                    switch (mode)
                    {
                        case TrayToolTipMode.Disabled:
                            Assert(taskbarIcon.TrayToolTip == null, "Disabled mode must not register WPF custom tooltip content.");
                            Assert(string.IsNullOrEmpty(taskbarIcon.ToolTipText), "Disabled mode must not provide native Shell tooltip text.");
                            Assert(!registration.SubscribesCustomOpenEvent, "Disabled mode must not subscribe to custom tooltip events.");
                            break;
                        case TrayToolTipMode.WindowsNative:
                            BindingOperations.GetBindingExpression(taskbarIcon, TaskbarIcon.ToolTipTextProperty)?.UpdateTarget();
                            Assert(taskbarIcon.TrayToolTip == null, "WindowsNative mode must not create WPF custom tooltip content.");
                            Assert(!string.IsNullOrWhiteSpace(taskbarIcon.ToolTipText), "WindowsNative mode must provide Shell tooltip text.");
                            Assert(taskbarIcon.ToolTipText.Length <= NativeTrayToolTipText.MaximumUtf16CodeUnits, "WindowsNative Shell text must respect the szTip limit.");
                            Assert(!registration.SubscribesCustomOpenEvent, "WindowsNative mode must not subscribe to custom tooltip events.");
                            break;
                        case TrayToolTipMode.PowerTrayCustom:
                            Assert(taskbarIcon.TrayToolTip != null, "PowerTrayCustom mode must register the themed WPF tooltip content.");
                            Assert(string.IsNullOrEmpty(taskbarIcon.ToolTipText), "PowerTrayCustom mode must not also register Shell tooltip text.");
                            Assert(registration.SubscribesCustomOpenEvent, "PowerTrayCustom mode must subscribe only to its custom-open event.");
                            resolvedCustomToolTip = taskbarIcon.TrayToolTipResolved;
                            Assert(resolvedCustomToolTip != null, "PowerTrayCustom mode should resolve an actual WPF ToolTip.");
                            break;
                    }

                    device.MarkOffline();
                    Assert(device.TaskbarIconForTesting == null, $"Mode {mode} must remove the tray icon through the production OFFLINE path.");
                    if (resolvedCustomToolTip != null)
                    {
                        Assert(!resolvedCustomToolTip.IsOpen, "Custom tooltip must be closed before icon disposal.");
                        Assert(resolvedCustomToolTip.Content == null, "Custom tooltip content must be detached during production icon disposal.");
                        Assert(resolvedCustomToolTip.DataContext == null, "Custom tooltip data context must be detached during production icon disposal.");
                    }

                    device.MarkPresence();
                    LogiDeviceIcon recreatedIcon = device.TaskbarIconForTesting
                        ?? throw new InvalidOperationException($"Mode {mode} should recreate the tray icon when the device returns online.");
                    Assert(!ReferenceEquals(firstIcon, recreatedIcon), $"Mode {mode} should create a fresh icon after OFFLINE recovery.");
                }
                finally
                {
                    PowerTrayConstants.UserDataDirectoryOverrideForTests = null;
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, recursive: true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            application?.Shutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(30));

    Assert(!thread.IsAlive, "Production tooltip lifecycle test must complete without a dispatcher hang.");
    if (failure != null)
    {
        throw new InvalidOperationException("Production tray tooltip mode lifecycle failed.", failure);
    }
}

static void TestLowBatteryAlertIcons()
{
    HashSet<string> fingerprints = [];
    foreach (DeviceType deviceType in new[] { DeviceType.Mouse, DeviceType.Keyboard, DeviceType.Headset })
    {
        LogiDevice device = new()
        {
            DeviceType = deviceType,
            BatteryPercentage = 5,
            PowerSupplyStatus = PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING,
        };

        using System.Drawing.Bitmap bitmap = BatteryIconDrawing.CreateAlertBitmap(device);
        int alertRedPixels = 0;
        int devicePixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                System.Drawing.Color pixel = bitmap.GetPixel(x, y);
                bool isAlertRed = pixel.A > 0 && pixel.R >= 0xD0 && pixel.G <= 0x40 && pixel.B <= 0x50;
                if (isAlertRed)
                {
                    alertRedPixels++;
                }
                else if (pixel.A > 0)
                {
                    devicePixels++;
                }
            }
        }

        Assert(alertRedPixels > 0, $"{deviceType} alert icon should contain a red battery layer.");
        Assert(devicePixels > 0, $"{deviceType} alert icon should retain its themed device glyph.");

        using MemoryStream encoded = new();
        bitmap.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
        fingerprints.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded.ToArray())));
    }

    Assert(fingerprints.Count == 3, "Mouse, keyboard, and headset alert icons should remain visually distinct.");
}

static void TestTrayMenuPaletteUsesApplicationThemeColors()
{
    ResourceDictionary resources = new();
    SolidColorBrush frozenTarget = new(Colors.White);
    frozenTarget.Freeze();
    resources["TrayMenuResolvedBackgroundBrush"] = frozenTarget;

    ThemeService.ApplyTrayMenuPalette(resources, light: false);

    SolidColorBrush darkBackground = (SolidColorBrush)resources["TrayMenuResolvedBackgroundBrush"];
    SolidColorBrush darkForeground = (SolidColorBrush)resources["TrayMenuResolvedForegroundBrush"];
    Assert(!ReferenceEquals(darkBackground, frozenTarget), "Tray palette refresh must replace a frozen resource instead of mutating it.");
    Assert(darkBackground.Color == Color.FromRgb(0x18, 0x1B, 0x21), "Dark tray menu background must match the PowerTray dark palette.");
    Assert(darkForeground.Color == Color.FromRgb(0xF3, 0xF4, 0xF6), "Dark tray menu text must match the PowerTray dark palette.");

    ThemeService.ApplyTrayMenuPalette(resources, light: true);
    SolidColorBrush lightBackground = (SolidColorBrush)resources["TrayMenuResolvedBackgroundBrush"];
    SolidColorBrush lightForeground = (SolidColorBrush)resources["TrayMenuResolvedForegroundBrush"];
    Assert(lightBackground.Color == Colors.White, "Light tray menu background must match the PowerTray light palette.");
    Assert(lightForeground.Color == Color.FromRgb(0x11, 0x18, 0x27), "Light tray menu text must match the PowerTray light palette.");
}

static void TestTrayMenuDictionaryDoesNotShadowApplicationPalette()
{
    Exception? failure = null;
    Thread thread = new(() =>
    {
        try
        {
            Application application = new();
            ThemeService.ApplyTrayMenuPalette(application.Resources, light: false);
            application.Resources["UIFontFamily"] = new FontFamily("Segoe UI");
            application.Resources["UIMenuFontSize"] = 12.5;

            ResourceDictionary dictionary = new()
            {
                Source = new Uri("/PowerTray;component/NotifyIconResources.xaml", UriKind.Relative),
            };
            application.Resources.MergedDictionaries.Add(dictionary);

            Assert(!dictionary.Contains("TrayMenuResolvedBackgroundBrush"), "Tray resource dictionary must not shadow the application theme background.");
            Assert(!dictionary.Contains("TrayMenuResolvedForegroundBrush"), "Tray resource dictionary must not shadow the application theme foreground.");

            ContextMenu menu = (ContextMenu)dictionary["SysTrayMenu"];
            SolidColorBrush background = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedBackgroundBrush");
            SolidColorBrush foreground = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedForegroundBrush");
            Assert(background.Color == Color.FromRgb(0x18, 0x1B, 0x21), "Tray menu must resolve the dark background from application resources.");
            Assert(foreground.Color == Color.FromRgb(0xF3, 0xF4, 0xF6), "Tray menu must resolve the dark foreground from application resources.");

            SolidColorBrush frozenBackground = new(Colors.White);
            frozenBackground.Freeze();
            menu.Resources["TrayMenuResolvedBackgroundBrush"] = frozenBackground;

            TrayContextMenuPlacement.RefreshThemeResources(menu, light: false);
            SolidColorBrush darkBackground = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedBackgroundBrush");
            SolidColorBrush darkForeground = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedForegroundBrush");
            MenuItem devicesMenu = (MenuItem)menu.Items[0];
            menu.ApplyTemplate();
            devicesMenu.ApplyTemplate();
            Assert(devicesMenu.MaxHeight == 112, "Tray device submenu must be capped at exactly four 28-DIP rows.");

            ScrollViewer CreateDeviceScrollViewer(int itemCount)
            {
                MenuItem testMenu = new()
                {
                    Style = (Style)dictionary["TrayMenuItemStyle"],
                    MaxHeight = devicesMenu.MaxHeight,
                    ItemsSource = Enumerable.Range(1, itemCount).Select(index => $"Device {index}").ToArray(),
                };
                testMenu.ApplyTemplate();

                return testMenu.Template.FindName("SubmenuScrollViewer", testMenu) as ScrollViewer
                    ?? throw new InvalidOperationException("Tray submenu template must expose its scroll viewer for validation.");
            }

            for (int deviceCount = 0; deviceCount <= 4; deviceCount++)
            {
                ScrollViewer boundedDeviceScrollViewer = CreateDeviceScrollViewer(deviceCount);
                Assert(boundedDeviceScrollViewer.MaxHeight == 112, "Tray submenu viewport must preserve the four-row height cap.");
                Assert(boundedDeviceScrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, $"{deviceCount} tray devices must not show or require a vertical scrollbar.");
            }

            ScrollViewer fiveDeviceScrollViewer = CreateDeviceScrollViewer(5);
            Assert(fiveDeviceScrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "Five tray devices must enable vertical scrolling when the viewport overflows.");

            ScrollBar trayScrollBar = new() { Style = (Style)dictionary["TrayMenuScrollBarStyle"] };
            Assert(trayScrollBar.Width == 9, "Tray submenu scrollbar must use the compact PowerTray width.");
            Assert(object.ReferenceEquals(trayScrollBar.Template, dictionary["TrayMenuVerticalScrollBarTemplate"]), "Tray submenu scrollbar must use the custom arrowless template.");

            Assert(!ReferenceEquals(darkBackground, frozenBackground), "Live tray menu palette refresh must replace a frozen popup resource.");
            Assert(darkBackground.Color == Color.FromRgb(0x18, 0x1B, 0x21), "Live tray menu popup background must refresh to dark without a restart.");
            Assert(darkForeground.Color == Color.FromRgb(0xF3, 0xF4, 0xF6), "Live tray menu popup text must refresh to dark without a restart.");
            Assert(((SolidColorBrush)menu.Background).Color == darkBackground.Color, "Live tray menu root background must use the refreshed dark palette.");
            Assert(((SolidColorBrush)devicesMenu.Foreground).Color == darkForeground.Color, "Live tray menu item text must use the refreshed dark palette.");

            TrayContextMenuPlacement.RefreshThemeResources(menu, light: true);
            SolidColorBrush lightBackground = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedBackgroundBrush");
            SolidColorBrush lightForeground = (SolidColorBrush)menu.TryFindResource("TrayMenuResolvedForegroundBrush");
            Assert(lightBackground.Color == Colors.White, "Live tray menu popup background must refresh to light without a restart.");
            Assert(lightForeground.Color == Color.FromRgb(0x11, 0x18, 0x27), "Live tray menu popup text must refresh to light without a restart.");
            Assert(((SolidColorBrush)menu.Background).Color == lightBackground.Color, "Live tray menu root background must update to light without a restart.");
            Assert(((SolidColorBrush)devicesMenu.Foreground).Color == lightForeground.Color, "Live tray menu item text must update to light without a restart.");

            application.Shutdown();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure != null)
    {
        throw new InvalidOperationException("Tray menu resource resolution test failed.", failure);
    }
}

static async Task TestDeferredOfflineGateDelaysOffline()
{
    List<string> emitted = [];
    using DeferredOfflineGate gate = new();

    gate.BeginDeferral("testHotplug", TimeSpan.FromMilliseconds(40));
    bool deferred = gate.TryDefer(new DeviceOfflineMessage("device-1"), message => emitted.Add(message.deviceId));

    Assert(deferred, "Offline message should be deferred during the grace window.");
    Assert(emitted.Count == 0, "Deferred offline message should not be emitted immediately.");

    await Task.Delay(120);
    Assert(emitted.Count == 1 && emitted[0] == "device-1", "Deferred offline message should be emitted after the grace window.");
}

static async Task TestDeferredOfflineGateCancelsOffline()
{
    List<string> emitted = [];
    using DeferredOfflineGate gate = new();

    gate.BeginDeferral("testHotplug", TimeSpan.FromMilliseconds(80));
    bool deferred = gate.TryDefer(new DeviceOfflineMessage("device-2"), message => emitted.Add(message.deviceId));
    bool cancelled = gate.Cancel("device-2");

    Assert(deferred, "Offline message should be deferred before cancellation.");
    Assert(cancelled, "Pending deferred offline message should be cancellable by device id.");

    await Task.Delay(160);
    Assert(emitted.Count == 0, "Cancelled deferred offline message should never be emitted.");
}

static void TestDeferredOfflineGatePassesThroughOutsideGraceWindow()
{
    List<string> emitted = [];
    using DeferredOfflineGate gate = new();

    bool deferred = gate.TryDefer(new DeviceOfflineMessage("device-3"), message => emitted.Add(message.deviceId));

    Assert(!deferred, "Offline message should not be deferred outside the grace window.");
    Assert(emitted.Count == 0, "Gate should not emit pass-through messages; the caller owns immediate emission.");
}

static async Task TestRediscoverySchedulerPreservesRequestDuringActivePass()
{
    TaskCompletionSource firstPassEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource releaseFirstPass = new(TaskCreationOptions.RunContinuationsAsynchronously);
    SemaphoreSlim passLock = new(1, 1);
    List<string> reasons = [];

    using RediscoveryScheduler scheduler = new(
        async (reason, _, cancellationToken) =>
        {
            await passLock.WaitAsync(cancellationToken);
            try
            {
                lock (reasons)
                {
                    reasons.Add(reason);
                }

                if (reason == "active")
                {
                    firstPassEntered.TrySetResult();
                    await releaseFirstPass.Task.WaitAsync(cancellationToken);
                }

                return false;
            }
            finally
            {
                passLock.Release();
            }
        },
        CancellationToken.None,
        [TimeSpan.Zero]
    );

    Task active = scheduler.RunNowAsync("active", CancellationToken.None);
    await firstPassEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Task queued = scheduler.ScheduleLatest(TimeSpan.Zero, "queued");
    releaseFirstPass.TrySetResult();
    await Task.WhenAll(active, queued).WaitAsync(TimeSpan.FromSeconds(2));

    Assert(reasons.SequenceEqual(["active", "queued"]), "A rediscovery request made during an active pass must run afterwards.");
}

static async Task TestRediscoveryArrivalBurstRunsEveryBoundedAttempt()
{
    Assert(
        RediscoveryScheduler.DefaultRetrySchedule.SequenceEqual(
            [
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromMilliseconds(1000),
                TimeSpan.FromMilliseconds(2500),
                TimeSpan.FromMilliseconds(5000),
            ]
        ),
        "Arrival recovery must use the bounded 0/300/1000/2500/5000ms schedule."
    );

    List<bool> retryModes = [];
    int attempts = 0;
    using RediscoveryScheduler scheduler = new(
        (_, retryIncompleteOnly, _) =>
        {
            retryModes.Add(retryIncompleteOnly);
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("transient arrival race");
            }

            return Task.FromResult(false);
        },
        CancellationToken.None,
        [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]
    );

    await scheduler.RestartArrivalBurst("arrival").WaitAsync(TimeSpan.FromSeconds(2));

    Assert(attempts == 3, "Arrival recovery must retain every bounded attempt even when an early pass fails or sees no incomplete session.");
    Assert(retryModes.SequenceEqual([false, true, true]), "Only the first arrival pass may refresh healthy sessions.");
}

static async Task TestRediscoveryIncompleteRetryStopsAfterRecovery()
{
    List<bool> retryModes = [];
    int attempts = 0;
    using RediscoveryScheduler scheduler = new(
        (_, retryIncompleteOnly, _) =>
        {
            retryModes.Add(retryIncompleteOnly);
            attempts++;
            return Task.FromResult(attempts < 2);
        },
        CancellationToken.None,
        [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]
    );

    await scheduler.RunIncompleteRetryAsync("emptySession", CancellationToken.None);

    Assert(attempts == 2, "Incomplete-session recovery should stop once every session has a known device.");
    Assert(retryModes.All(retryOnly => retryOnly), "Incomplete-session recovery must never refresh healthy sessions.");
}

static void TestHidSessionRecoveryPolicy()
{
    Assert(
        HidSessionRecoveryPolicy.ShouldBypassOfflineDeferral(false, [0x00]),
        "A confirmed direct device at index 00 should bypass the receiver offline grace period."
    );
    Assert(
        HidSessionRecoveryPolicy.ShouldBypassOfflineDeferral(false, [0xFF]),
        "A confirmed direct device at index FF should bypass the receiver offline grace period."
    );
    Assert(
        !HidSessionRecoveryPolicy.ShouldBypassOfflineDeferral(true, [0xFF]),
        "A detected receiver must retain the offline grace period even when index FF is present."
    );
    Assert(
        !HidSessionRecoveryPolicy.ShouldBypassOfflineDeferral(false, [0x01, 0x02]),
        "LIGHTSPEED pairing slots must retain the receiver offline grace period."
    );
    Assert(
        HidSessionRecoveryPolicy.ShouldEmitBypassedOffline(false, false),
        "The first confirmed direct-device removal must emit immediately."
    );
    Assert(
        HidSessionRecoveryPolicy.ShouldEmitBypassedOffline(true, true),
        "A confirmed direct-device removal must promote an already deferred offline signal."
    );
    Assert(
        !HidSessionRecoveryPolicy.ShouldEmitBypassedOffline(true, false),
        "A repeated direct-device removal must not duplicate an offline signal that was already emitted."
    );
}

static void TestRediscoveryPassPrioritizesCreatedSessions()
{
    string[] sessions = ["reused-a", "created-a", "reused-b", "created-b"];
    IReadOnlyList<string> ordered = RediscoveryPassPolicy.PrioritizeCreated(
        sessions,
        ["created-a", "created-b"]
    );

    Assert(
        ordered.SequenceEqual(["created-a", "created-b", "reused-a", "reused-b"]),
        "New HID sessions must be processed first while preserving order inside both groups."
    );
    Assert(
        ReferenceEquals(
            sessions,
            RediscoveryPassPolicy.PrioritizeCreated(sessions, Array.Empty<string>())
        ),
        "A pass without new sessions should retain the original session list."
    );
}

static void TestHidCommandAttemptPolicy()
{
    Assert(
        HidCommandAttemptPolicy.GetAttempts(false, 1) == 1,
        "Speculative non-C54D probes should be allowed to use one attempt."
    );
    Assert(
        HidCommandAttemptPolicy.GetAttempts(false, null) == 2,
        "Normal HID++ commands must retain two attempts."
    );
    Assert(
        HidCommandAttemptPolicy.GetAttempts(true, 1) == 2,
        "C54D short-report recovery must retain two attempts even for speculative probes."
    );
    Assert(
        HidCommandAttemptPolicy.ShouldUseC54dRecovery(
            true, true, true, true, 7, 0x10, 0x01
        ),
        "Normal C54D slot commands should retain robust recovery."
    );
    Assert(
        !HidCommandAttemptPolicy.ShouldUseC54dRecovery(
            false, true, true, true, 7, 0x10, 0x01
        ),
        "The bounded optimistic cached-slot probe should be able to bypass C54D recovery once."
    );
    Assert(
        !HidCommandAttemptPolicy.ShouldUseC54dRecovery(
            true, true, true, true, 7, 0x10, 0xFF
        ),
        "Receiver-register traffic must remain outside C54D device-slot recovery."
    );
}

static void TestDeviceTransportPolicy()
{
    Assert(DeviceTransportPolicy.GetPresenceConfirmationAttempts(1) == 2, "Presence confirmation must retain a minimum of two attempts.");
    Assert(DeviceTransportPolicy.GetPresenceConfirmationAttempts(3) == 3, "Presence confirmation must use the configured failure threshold in one check.");
    Assert(!DeviceTransportPolicy.ShouldSignalOffline(1, 3), "A single transient failure must not mark a device offline.");
    Assert(!DeviceTransportPolicy.ShouldSignalOffline(2, 3), "Two failures must remain below a threshold of three.");
    Assert(DeviceTransportPolicy.ShouldSignalOffline(3, 3), "The configured consecutive-failure threshold should mark the device offline.");

    BatteryUpdateReturn unchanged = new(50, PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 3900);
    Assert(!DeviceTransportPolicy.ShouldPublishUpdate(false, false, unchanged, unchanged), "Unchanged online battery state should remain suppressed.");
    Assert(DeviceTransportPolicy.ShouldPublishUpdate(false, true, unchanged, unchanged), "Offline recovery must publish even when battery data is unchanged.");
    Assert(DeviceTransportPolicy.ShouldPublishUpdate(true, false, unchanged, unchanged), "A forced update must always publish.");
}

static void TestNativeSettingsValidation()
{
    NativeDeviceManagerSettings defaults = new();
    Assert(defaults.PresencePeriod == 15, "Native presence checks should default to the safe 15-second minimum.");

    NativeDeviceManagerSettings settings = new()
    {
        RetryTime = -1,
        PollPeriod = int.MaxValue,
        PresencePeriod = 1,
        ConsecutiveFailureThreshold = 99,
        DisabledDevices = ["", "  ", "g733", "G733"],
    };

    Assert(settings.RetryTime == 1, "RetryTime should clamp to its minimum.");
    Assert(settings.PollPeriod == 86400, "PollPeriod should clamp to its maximum.");
    Assert(settings.PresencePeriod == 15, "PresencePeriod should clamp to its minimum.");
    Assert(settings.ConsecutiveFailureThreshold == 10, "Failure threshold should clamp to its maximum.");
    Assert(settings.DisabledDevices.SequenceEqual(["g733"], StringComparer.OrdinalIgnoreCase), "Disabled device filters should ignore blanks and duplicates.");
}

static void TestCenturionFrameValidation()
{
    byte[] payload = [0x01, 0x02, 0x03];
    byte[] frame = CenturionFrameCodec.BuildFrame(CenturionFrameCodec.ReportId, null, payload);
    Assert(CenturionFrameCodec.TryExtractPayload(frame, out byte reportId, out byte? address, out byte[] extracted), "A valid Centurion frame should decode.");
    Assert(reportId == CenturionFrameCodec.ReportId && address == null && extracted.SequenceEqual(payload), "Decoded Centurion payload should match the input.");

    byte[] malformed = (byte[])frame.Clone();
    malformed[1] = 63;
    Assert(!CenturionFrameCodec.TryExtractPayload(malformed, out _, out _, out _), "A declared payload longer than the frame must be rejected.");
    AssertThrows<ArgumentOutOfRangeException>(() => CenturionFrameCodec.BuildFrame(CenturionFrameCodec.AddressedReportId, 1, new byte[61]), "Oversized addressed payloads must be rejected.");
}

static void TestCenturionConnectionNotificationDecode()
{
    const byte bridgeIndex = 0x03;
    byte[] connected = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x00, 0x00, 0x01]
    );
    Assert(
        CenturionConnectionNotificationCodec.TryDecode(
            connected,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out CenturionConnectionState connectedState
        ) && connectedState == CenturionConnectionState.Connected,
        "A non-empty Centurion sub-device list should decode as connected."
    );

    byte[] disconnected = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x00, 0x00, 0x00]
    );
    Assert(
        CenturionConnectionNotificationCodec.TryDecode(
            disconnected,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out CenturionConnectionState disconnectedState
        ) && disconnectedState == CenturionConnectionState.Disconnected,
        "An empty Centurion sub-device list should decode as disconnected."
    );

    byte[] connectedWithType = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x00, 0xA0, 0x02]
    );
    Assert(
        CenturionConnectionNotificationCodec.TryDecode(
            connectedWithType,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out CenturionConnectionState typedState
        ) && typedState == CenturionConnectionState.Connected,
        "The connection type nibble should not alter the descriptor-list length."
    );

    byte[] wrongBridge = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [0x04, 0x00, 0x00, 0x01]
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            wrongBridge,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A notification for another bridge must be rejected."
    );

    byte[] response = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x01, 0x00, 0x01]
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            response,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A non-zero software id must not be treated as an unsolicited notification."
    );

    byte[] addressed = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.AddressedReportId,
        0x2A,
        [bridgeIndex, 0x00, 0x00, 0x01]
    );
    Assert(
        CenturionConnectionNotificationCodec.TryDecode(
            addressed,
            CenturionFrameCodec.AddressedReportId,
            0x2A,
            bridgeIndex,
            out CenturionConnectionState addressedState
        ) && addressedState == CenturionConnectionState.Connected,
        "An addressed notification should require and accept the active address."
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            addressed,
            CenturionFrameCodec.AddressedReportId,
            0x2B,
            bridgeIndex,
            out _
        ),
        "An addressed notification for another device address must be rejected."
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            connected,
            CenturionFrameCodec.AddressedReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A notification using another report id must be rejected."
    );

    byte[] wrongFunction = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x10, 0x00, 0x01]
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            wrongFunction,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A non-connection Centurion event must be rejected."
    );

    byte[] shortPayload = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x00, 0x00]
    );
    Assert(
        !CenturionConnectionNotificationCodec.TryDecode(
            shortPayload,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A truncated connection notification must be rejected."
    );
}

static void TestCenturionBridgeNotificationDecode()
{
    const byte bridgeIndex = 0x03;
    const byte batteryIndex = 0x05;
    byte[] notificationFrame = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x10, 0x00, 0x06, 0xFF, batteryIndex, 0x00, 84, 84, 0]
    );
    Assert(
        CenturionBridgeNotificationCodec.TryDecode(
            notificationFrame,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out CenturionBridgeNotification? notification
        ) &&
        notification != null &&
        notification.FeatureIndex == batteryIndex &&
        notification.Function == 0 &&
        notification.Data.SequenceEqual(new byte[] { 84, 84, 0 }),
        "An unsolicited Centurion bridge MessageEvent should decode its feature and payload."
    );

    byte[] responseFrame = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x10, 0x00, 0x06, 0x00, batteryIndex, 0x0A, 84, 84, 0]
    );
    Assert(
        !CenturionBridgeNotificationCodec.TryDecode(
            responseFrame,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A solicited bridge response must not be treated as a notification."
    );

    byte[] wrongLength = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.ReportId,
        null,
        [bridgeIndex, 0x10, 0x00, 0x07, 0xFF, batteryIndex, 0x00, 84, 84, 0]
    );
    Assert(
        !CenturionBridgeNotificationCodec.TryDecode(
            wrongLength,
            CenturionFrameCodec.ReportId,
            null,
            bridgeIndex,
            out _
        ),
        "A bridge MessageEvent with a mismatched declared length must be rejected."
    );

    byte[] wrongAddress = CenturionFrameCodec.BuildFrame(
        CenturionFrameCodec.AddressedReportId,
        0x2A,
        [bridgeIndex, 0x10, 0x00, 0x06, 0xFF, batteryIndex, 0x00, 84, 84, 0]
    );
    Assert(
        !CenturionBridgeNotificationCodec.TryDecode(
            wrongAddress,
            CenturionFrameCodec.AddressedReportId,
            0x2B,
            bridgeIndex,
            out _
        ),
        "An addressed bridge MessageEvent for another Centurion address must be rejected."
    );
}

static void TestCenturionFeatureMetadataDecode()
{
    IReadOnlyList<CenturionFeatureDescriptor> bulk = CenturionFeatureSetCodec.DecodeEntries(
        [0x02, 0x01, 0x04, 0x02, 0x01, 0x01, 0x08, 0x01, 0x05],
        0x05
    );
    Assert(
        bulk.Count == 2 &&
        bulk[0] == new CenturionFeatureDescriptor(0x0104, 0x05, 0x02, 0x01) &&
        bulk[1] == new CenturionFeatureDescriptor(0x0108, 0x06, 0x01, 0x05),
        "Bulk Centurion feature entries should retain index, type, and version."
    );

    IReadOnlyList<CenturionFeatureDescriptor> last = CenturionFeatureSetCodec.DecodeEntries(
        [0x00, 0x06, 0x36, 0x00, 0x01],
        0x09
    );
    Assert(
        last.Count == 1 &&
        last[0] == new CenturionFeatureDescriptor(0x0636, 0x09, 0x00, 0x01),
        "The final per-index Centurion feature entry should decode when remaining count is zero."
    );
}

static void TestCenturionReadOnlyDeviceInfoDecode()
{
    Assert(
        CenturionDeviceInfoCodec.TryDecodeHardware(
            [0x02, 0x07, 0x0A, 0xF7],
            out CenturionHardwareInfo? hardware
        ) &&
        hardware == new CenturionHardwareInfo(0x02, 0x07, 0x0AF7),
        "Centurion hardware information should decode model, revision, and product id."
    );
    Assert(
        CenturionDeviceInfoCodec.TryDecodeFirmware(
            [0x01, 0x00, 0x02, 0x07, 0x03, (byte)'A', (byte)'B', (byte)'C'],
            out CenturionFirmwareInfo? firmware
        ) &&
        firmware == new CenturionFirmwareInfo(0x01, "ABC", "2.07"),
        "Centurion firmware information should decode type, name, and version."
    );
    Assert(
        !CenturionDeviceInfoCodec.TryDecodeFirmware(
            [0x01, 0x00, 0x02, 0x07, 0x08, (byte)'A'],
            out _
        ),
        "A truncated Centurion firmware name must be rejected."
    );
}

static void TestCenturionEndpointCandidatePolicy()
{
    HidEndpointInfo unknownCenturion = new(
        "unknown-centurion",
        Guid.NewGuid(),
        0x046D,
        0x0C01,
        0x0100,
        "Logitech",
        "Future Centurion Device",
        null,
        "path-hash",
        "opened",
        0xFFA0,
        0x01,
        3,
        HidppMessageType.CENTURION
    );
    Assert(
        KnownLogitechDevices.IsCenturionEndpointCandidate(unknownCenturion),
        "An unknown Logitech product should be a Centurion candidate when its HID descriptor identifies the protocol."
    );
    Assert(
        !KnownLogitechDevices.TryGetCenturionReportId(unknownCenturion.ProductId, out _),
        "An unknown Centurion candidate must negotiate its report id instead of inheriting a guessed model mapping."
    );
    Assert(
        KnownLogitechDevices.TryGetCenturionReportId(0x0AF7, out byte proX2ReportId) &&
        proX2ReportId == CenturionFrameCodec.ReportId &&
        KnownLogitechDevices.TryGetCenturionReportId(0x0B18, out byte g522ReportId) &&
        g522ReportId == CenturionFrameCodec.AddressedReportId,
        "Known Centurion products should keep their validated report-id mappings."
    );
    Assert(
        !KnownLogitechDevices.IsCenturionEndpointCandidate(unknownCenturion with
        {
            UsagePage = 0xFF00,
        }),
        "An endpoint outside the Centurion HID usage page must not enter protocol probing."
    );
}

static void TestDiagnosticsPrivacyScope()
{
    Guid containerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    HidEndpointInfo logitech = new(
        "logitech-path", containerId, 0x046D, 0xC547, 0x0100, "Logitech", "Receiver",
        "serial-hash", "path-hash", "openFailed", 0xFF00, 0x01, 2, HidppMessageType.NONE);
    HidEndpointInfo otherVendor = new(
        "other-path", Guid.NewGuid(), 0x1234, 0x5678, 0x0100, "Other", "Security Key",
        "other-serial", "other-path-hash", "openFailed", 0xF1D0, 0x01, 1, HidppMessageType.NONE);

    NativeDiagnosticsStore.BeginDiscovery([logitech, otherVendor]);
    string json = NativeDiagnosticsStore.GetJson();
    Assert(!json.Contains(containerId.ToString("N"), StringComparison.OrdinalIgnoreCase), "Diagnostics must not contain a plaintext ContainerId.");
    Assert(!json.Contains("Security Key", StringComparison.OrdinalIgnoreCase), "Diagnostics must exclude non-Logitech devices.");
    Assert(json.Contains("containerIdHash", StringComparison.OrdinalIgnoreCase), "Diagnostics should retain only a ContainerId hash.");
    Assert(json.Contains("groupKeyHash", StringComparison.OrdinalIgnoreCase), "Diagnostics should retain only a group-key hash.");
}

static async Task TestDirectionalNamedPipeIpcAsync()
{
    ServiceCollection uiServices = new();
    uiServices.AddLGSMessagePipe(hostAsServer: true);
    ServiceCollection hidServices = new();
    hidServices.AddLGSMessagePipe(hostAsServer: false);

    await using ServiceProvider uiProvider = uiServices.BuildServiceProvider();
    await using ServiceProvider hidProvider = hidServices.BuildServiceProvider();

    IDistributedSubscriber<IPCMessageType, IPCMessage> uiSubscriber =
        uiProvider.GetRequiredService<IDistributedSubscriber<IPCMessageType, IPCMessage>>();
    IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage> uiPublisher =
        uiProvider.GetRequiredService<IDistributedPublisher<IPCMessageRequestType, IPCRequestMessage>>();
    IDistributedPublisher<IPCMessageType, IPCMessage> hidPublisher =
        hidProvider.GetRequiredService<IDistributedPublisher<IPCMessageType, IPCMessage>>();
    IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage> hidSubscriber =
        hidProvider.GetRequiredService<IDistributedSubscriber<IPCMessageRequestType, IPCRequestMessage>>();

    TaskCompletionSource<IPCMessage> heartbeatReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<IPCRequestMessage> requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    await using IAsyncDisposable messageSubscription = await uiSubscriber.SubscribeAsync(
        IPCMessageType.HEARTBEAT,
        message => heartbeatReceived.TrySetResult(message));
    await using IAsyncDisposable requestSubscription = await hidSubscriber.SubscribeAsync(
        IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST,
        request => requestReceived.TrySetResult(request));

    HeartbeatMessage heartbeat = new(1234, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "running");
    NativeHealthCheckRequestMessage request = new("directional-ipc-test");
    await hidPublisher.PublishAsync(IPCMessageType.HEARTBEAT, heartbeat);
    await uiPublisher.PublishAsync(IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST, request);

    IPCMessage receivedHeartbeat = await heartbeatReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    IPCRequestMessage receivedRequest = await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(receivedHeartbeat is HeartbeatMessage, "HID-to-UI messages must cross the dedicated named pipe.");
    Assert(receivedRequest is NativeHealthCheckRequestMessage, "UI-to-HID requests must cross the dedicated named pipe.");
}

static void TestIpcSessionAuthentication()
{
    IpcSessionContext.SetToken(new string('a', 64));

    InitMessage valid = new("device-1", "Test", true, DeviceType.Mouse);
    IpcSessionContext.Sign(IPCMessageType.INIT, valid);
    Assert(string.IsNullOrEmpty(valid.authTag) == false, "Signed IPC messages should contain an authentication tag.");
    Assert(!valid.authTag.Equals(new string('a', 64), StringComparison.OrdinalIgnoreCase), "IPC messages must not expose the session key as their authentication tag.");
    Assert(IpcSessionContext.Validate(IPCMessageType.INIT, valid), "A correctly signed IPC message should validate.");
    Assert(!IpcSessionContext.Validate(IPCMessageType.INIT, valid), "A signed IPC message nonce must not be accepted twice.");

    UpdateMessage tampered = new(
        "device-1", 50, PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING, 3900, DateTimeOffset.UtcNow);
    IpcSessionContext.Sign(IPCMessageType.UPDATE, tampered);
    tampered.batteryPercentage = 5;
    Assert(!IpcSessionContext.Validate(IPCMessageType.UPDATE, tampered), "Changing a signed IPC payload must invalidate its HMAC.");

    DeviceOfflineMessage wrongType = new("device-1");
    IpcSessionContext.Sign(IPCMessageType.OFFLINE, wrongType);
    Assert(!IpcSessionContext.Validate(IPCMessageType.INIT, wrongType), "An IPC signature must be bound to its message type.");

    IPCRequestMessage[] requests =
    [
        new NativeDiagnosticsRequestMessage("diagnostics"),
        new NativeRediscoverRequestMessage("rediscover"),
        new NativeHealthCheckRequestMessage("health"),
        new BatteryUpdateRequestMessage(),
    ];
    IPCMessageRequestType[] requestTypes =
    [
        IPCMessageRequestType.NATIVE_DIAGNOSTICS_REQUEST,
        IPCMessageRequestType.NATIVE_REDISCOVER_REQUEST,
        IPCMessageRequestType.NATIVE_HEALTH_CHECK_REQUEST,
        IPCMessageRequestType.BATTERY_UPDATE_REQUEST,
    ];
    for (int index = 0; index < requests.Length; index++)
    {
        IpcSessionContext.Sign(requestTypes[index], requests[index]);
        Assert(IpcSessionContext.Validate(requestTypes[index], requests[index]), $"IPC request type {requestTypes[index]} should validate.");
    }

    NativeRediscoverResponseMessage rediscoverResponse = new("rediscover-response");
    IpcSessionContext.Sign(IPCMessageType.NATIVE_REDISCOVER_RESPONSE, rediscoverResponse);
    Assert(IpcSessionContext.Validate(IPCMessageType.NATIVE_REDISCOVER_RESPONSE, rediscoverResponse), "Rediscover responses should validate.");

    InitMessage malformed = new("device-2", "Malformed", true, DeviceType.Mouse);
    malformed.nonce = null!;
    malformed.authTag = null!;
    Assert(!IpcSessionContext.Validate(IPCMessageType.INIT, malformed), "Null IPC envelope fields must be rejected without throwing.");

    InitMessage minimumTimestamp = new("device-3", "Minimum timestamp", true, DeviceType.Mouse);
    IpcSessionContext.Sign(IPCMessageType.INIT, minimumTimestamp);
    minimumTimestamp.issuedAtUnixMilliseconds = long.MinValue;
    Assert(!IpcSessionContext.Validate(IPCMessageType.INIT, minimumTimestamp), "The minimum Int64 timestamp must be rejected without overflowing.");

    InitMessage maximumTimestamp = new("device-4", "Maximum timestamp", true, DeviceType.Mouse);
    IpcSessionContext.Sign(IPCMessageType.INIT, maximumTimestamp);
    maximumTimestamp.issuedAtUnixMilliseconds = long.MaxValue;
    Assert(!IpcSessionContext.Validate(IPCMessageType.INIT, maximumTimestamp), "The maximum Int64 timestamp must be rejected without overflowing.");
}

static async Task TestUpdaterDetachedSignatureVerificationAsync()
{
    byte[] checksumBytes = System.Text.Encoding.UTF8.GetBytes("PowerTray signed checksum fixture\n");
    using System.Security.Cryptography.ECDsa signer =
        System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    string fixturePublicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
    byte[] signatureBytes = signer.SignData(
        checksumBytes,
        System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation
    );

    Assert(await UpdateService.VerifyChecksumSignatureAsync(checksumBytes, signatureBytes, fixturePublicKey), "A checksum signed by a standard P-256 test key should validate.");
    checksumBytes[0] ^= 0x01;
    Assert(!await UpdateService.VerifyChecksumSignatureAsync(checksumBytes, signatureBytes, fixturePublicKey), "A modified checksum must fail detached signature verification.");
    Assert(!await UpdateService.VerifyChecksumSignatureAsync(checksumBytes, signatureBytes), "A signature from a non-production key must not validate against the pinned release key.");
}

static async Task TestUpdaterFileHashVerificationAsync()
{
    string path = Path.Combine(Path.GetTempPath(), $"PowerTray-update-test-{Guid.NewGuid():N}.bin");
    try
    {
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4])).ToLowerInvariant();
        Assert(await UpdateService.VerifyFileHashAsync(path, expected), "The updater should accept an unchanged verified file.");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 5]);
        Assert(!await UpdateService.VerifyFileHashAsync(path, expected), "The updater should reject a file replaced after validation.");
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

static void TestUpdaterTrustedHosts()
{
    Assert(UpdateService.IsTrustedDownloadUri("https://github.com/JumpTwiceShou/PowerTray/releases/download/v1/PowerTraySetup.exe"), "GitHub HTTPS release URLs should be trusted.");
    Assert(UpdateService.IsTrustedDownloadUri("https://release-assets.githubusercontent.com/example"), "GitHub release asset CDN should be trusted.");
    Assert(!UpdateService.IsTrustedDownloadUri("http://github.com/example"), "HTTP update URLs must be rejected.");
    Assert(!UpdateService.IsTrustedDownloadUri("https://github.com.evil.example/file"), "Lookalike update hosts must be rejected.");
    Assert(!UpdateService.IsTrustedDownloadUri("https://user@github.com/file"), "Update URLs with embedded credentials must be rejected.");
}

static void TestEndpointReceiverIdentityValidation()
{
    Guid containerId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    HidEndpointInfo invalidSerial = new(
        "path", containerId, 0x046D, 0xC547, 0x0100, "Logitech", "Receiver",
        string.Empty, "path-hash", "opened", 0xFF00, 0x01, 2, HidppMessageType.SHORT);
    Assert(invalidSerial.ReceiverStableId == $"container:{containerId:N}", "An invalid receiver serial must fall back to ContainerId.");

    HidEndpointInfo validSerial = new(
        "path", containerId, 0x046D, 0xC547, 0x0100, "Logitech", "Receiver",
        "serial-hash", "path-hash", "opened", 0xFF00, 0x01, 2, HidppMessageType.SHORT);
    Assert(validSerial.ReceiverStableId == "serial:serial-hash", "A validated receiver serial hash should take precedence over ContainerId.");
}

static void TestHidDeviceIndexCache()
{
    HidDeviceIndexCache.ClearForTests();
    try
    {
        Guid containerId = Guid.Parse("a5b4c3d2-e1f0-4a5b-8c7d-6e5f4a3b2c1d");
        HidEndpointInfo first = new(
            "path-a", containerId, 0x046D, 0xCAFE, 0x0100, "Logitech", "Device",
            "stable-serial", "path-hash-a", "opened", 0xFF00, 0x01, 2, HidppMessageType.SHORT);
        HidEndpointInfo movedPath = new(
            "path-b", containerId, 0x046D, 0xCAFE, 0x0100, "Logitech", "Device",
            "stable-serial", "path-hash-b", "opened", 0xFF00, 0x01, 2, HidppMessageType.SHORT);
        HidEndpointInfo differentDevice = first with
        {
            Path = "path-c",
            SerialNumberHash = "different-serial",
            PathHash = "path-hash-c",
        };
        HidEndpointInfo invalidIndexDevice = first with
        {
            ProductId = 0xCAFF,
            SerialNumberHash = "invalid-index",
        };
        HidEndpointInfo unstableIdentity = first with
        {
            ContainerId = Guid.Empty,
            SerialNumberHash = null,
        };

        Assert(
            HidDeviceIndexCache.RememberDirect(first, 0xFF),
            "A protocol-confirmed direct FF index with stable endpoint identity should be cached."
        );
        Assert(
            HidDeviceIndexCache.TryGetDirect(movedPath, out byte cachedIndex) && cachedIndex == 0xFF,
            "The direct index cache should survive endpoint path and path-hash changes."
        );
        Assert(
            !HidDeviceIndexCache.TryGetDirect(differentDevice, out _),
            "A different stable endpoint identity must not inherit another device's direct index."
        );
        Assert(
            !HidDeviceIndexCache.RememberDirect(invalidIndexDevice, 0x01) &&
            !HidDeviceIndexCache.TryGetDirect(invalidIndexDevice, out _),
            "Receiver pairing slots must never enter the direct-device cache."
        );
        Assert(
            !HidDeviceIndexCache.RememberDirect(unstableIdentity, 0x00) &&
            !HidDeviceIndexCache.TryGetDirect(unstableIdentity, out _),
            "Endpoints without a stable serial or container identity must not be cached."
        );
        Assert(
            HidDeviceIndexCache.RememberReceiverSlot(first, 0x03) &&
            HidDeviceIndexCache.RememberReceiverSlot(movedPath, 0x01),
            "Protocol-confirmed receiver slots should share the stable path-independent endpoint cache."
        );
        Assert(
            HidDeviceIndexCache.GetReceiverSlots(first).SequenceEqual([(byte)0x01, (byte)0x03]),
            "Receiver cache values must retain every confirmed slot in deterministic order."
        );
        Assert(
            !HidDeviceIndexCache.TryGetDirect(first, out _),
            "Remembering receiver transport must clear incompatible direct-index state."
        );
        Assert(
            !HidDeviceIndexCache.RememberReceiverSlot(first, 0x00) &&
            !HidDeviceIndexCache.RememberReceiverSlot(first, 0xFF),
            "Direct indexes must never enter the receiver-slot cache."
        );
        Assert(
            !HidDeviceIndexCache.RememberReceiverSlot(unstableIdentity, 0x01),
            "Receiver slots without a stable endpoint identity must not be cached."
        );
        Assert(
            HidDeviceIndexCache.RememberDirect(first, 0x00) &&
            HidDeviceIndexCache.GetReceiverSlots(first).Count == 0,
            "Remembering direct transport must clear incompatible receiver-slot state."
        );
    }
    finally
    {
        HidDeviceIndexCache.ClearForTests();
    }
}

static void TestPersistentReceiverIdentity()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), "PowerTray.Tests", Guid.NewGuid().ToString("N"));
    string storePath = Path.Combine(tempDirectory, "identities.json");
    Environment.SetEnvironmentVariable("POWERTRAY_TEST_IDENTITY_STORE", storePath);
    PersistentDeviceIdentityStore.ResetForTests();
    try
    {
        HidppDeviceIdentity first = HidppDeviceIdentity.FromDeviceInformation(
            "Test Device", 0xC547, 1, 2, "receiver:stable", "path-a", null, null, null, null);
        HidppDeviceIdentity repeated = HidppDeviceIdentity.FromDeviceInformation(
            "Test Device", 0xC547, 1, 2, "receiver:stable", "path-b", null, null, null, null);
        HidppDeviceIdentity secondSlot = HidppDeviceIdentity.FromDeviceInformation(
            "Test Device", 0xC547, 2, 2, "receiver:stable", "path-a", null, null, null, null);

        Assert(first.Identifier == repeated.Identifier, "Receiver identity plus pairing slot should survive endpoint-path changes.");
        Assert(first.Identifier != secondSlot.Identifier, "Different receiver pairing slots must not share an identifier.");
        Assert(first.Source == "receiverPairingSlot", "Fallback identity should record its receiver pairing source.");
    }
    finally
    {
        PersistentDeviceIdentityStore.ResetForTests();
        Environment.SetEnvironmentVariable("POWERTRAY_TEST_IDENTITY_STORE", null);
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

static void TestSettingsFileStore()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), $"PowerTray-settings-test-{Guid.NewGuid():N}");
    string settingsPath = Path.Combine(tempDirectory, "settings.json");
    try
    {
        SettingsFileStore.WriteAtomic(settingsPath, "{\"generation\":1}");
        SettingsFileStore.WriteAtomic(settingsPath, "{\"generation\":2}");
        Assert(File.ReadAllText(settingsPath).Contains("\"generation\":2", StringComparison.Ordinal), "Atomic settings writes should replace the destination.");
        Assert(File.Exists(settingsPath + ".bak"), "Replacing settings should retain the previous file as a backup.");
        Assert(File.ReadAllText(settingsPath + ".bak").Contains("\"generation\":1", StringComparison.Ordinal), "The settings backup should contain the previous generation.");

        Parallel.For(0, 32, index =>
            SettingsFileStore.WriteAtomic(settingsPath, $"{{\"generation\":{index}}}"));
        using JsonDocument finalSettings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert(finalSettings.RootElement.TryGetProperty("generation", out _), "Concurrent settings saves must leave a complete JSON document.");
        Assert(Directory.GetFiles(tempDirectory, "settings.json.*.tmp").Length == 0, "Successful settings saves should not leave temporary files.");

        SettingsSaveCoordinator coordinator = new();
        int currentGeneration = 1;
        string? persistedContent = null;
        using ManualResetEventSlim firstSnapshotCaptured = new(false);
        using ManualResetEventSlim allowFirstSnapshotToContinue = new(false);
        Task firstSave = Task.Run(() => coordinator.Save(
            () =>
            {
                int capturedGeneration = Volatile.Read(ref currentGeneration);
                firstSnapshotCaptured.Set();
                allowFirstSnapshotToContinue.Wait();
                return $"{{\"generation\":{capturedGeneration}}}";
            },
            content => persistedContent = content
        ));
        Assert(firstSnapshotCaptured.Wait(TimeSpan.FromSeconds(5)), "The first coordinated settings snapshot should start.");

        Volatile.Write(ref currentGeneration, 2);
        Task secondSave = Task.Run(() => coordinator.Save(
            () => $"{{\"generation\":{Volatile.Read(ref currentGeneration)}}}",
            content => persistedContent = content
        ));
        Assert(
            SpinWait.SpinUntil(() => coordinator.RequestedRevision >= 2, TimeSpan.FromSeconds(5)),
            "The newer settings save should register while the older snapshot is delayed."
        );
        allowFirstSnapshotToContinue.Set();
        Task.WaitAll(firstSave, secondSave);
        Assert(
            persistedContent?.Contains("\"generation\":2", StringComparison.Ordinal) == true,
            "A delayed older snapshot must not overwrite a newer settings generation."
        );
    }
    finally
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

static void TestCrashLogWriter()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), $"PowerTray-crashlog-test-{Guid.NewGuid():N}");
    try
    {
        InvalidOperationException exception = new("crash fixture");
        Assert(CrashLogWriter.TryWrite(exception, tempDirectory, new DateTimeOffset(2026, 7, 25, 12, 34, 56, TimeSpan.Zero)), "Crash logging should succeed in a writable user-data directory.");

        string[] logs = Directory.GetFiles(tempDirectory, "crashlog_*.log");
        Assert(logs.Length == 1, "Crash logging should create exactly one uniquely named log file.");
        Assert(File.ReadAllText(logs[0]).Contains("crash fixture", StringComparison.Ordinal), "Crash logging should preserve the original exception details.");
        Assert(!CrashLogWriter.TryWrite(exception, "\0"), "Crash logging should fail safely for an invalid path.");
    }
    finally
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

if (args.Contains("--tooltip-lifecycle-child", StringComparer.Ordinal))
{
    try
    {
        TestProductionTrayToolTipModeLifecycleCore();
        Console.WriteLine("Isolated production tooltip lifecycle passed.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Contains("--restart-wait-child", StringComparer.Ordinal))
{
    Thread.Sleep(400);
    return;
}

TestXmlEscaping();
TestLastUpdateDoesNotWriteDeviceMetadataToConsole();
TestBattery1F20Decode();
TestBattery1001LookupBoundaries();
TestHidDeviceInfoX64AbiLayout();
TestNativeIdentityDiagnosticsRedaction();
TestUpdaterAssetSelectionAndChecksum();
TestHttpServerLoopbackFallback();
TestHidHotplugRegistrationPolicy();
TestTrayToolTipSeparators();
TestTrayToolTipModesAndSettingsMigration();
TestNativeTrayToolTipLength();
TestLocalizationCatalogs();
await TestBatteryPollingLoopRecoversAfterUnexpectedFailureAsync();
TestMessagePipeDiagnosticsPolicy();
TestRestartWaitArgumentParsing();
TestRestartWaitProcessHandle();
TestTrayToolTipDisposalLifecycle();
TestProductionTrayToolTipModeLifecycle();
TestLowBatteryAlertIcons();
TestTrayMenuPaletteUsesApplicationThemeColors();
TestTrayMenuDictionaryDoesNotShadowApplicationPalette();
await TestDeferredOfflineGateDelaysOffline();
await TestDeferredOfflineGateCancelsOffline();
TestDeferredOfflineGatePassesThroughOutsideGraceWindow();
await TestRediscoverySchedulerPreservesRequestDuringActivePass();
await TestRediscoveryArrivalBurstRunsEveryBoundedAttempt();
await TestRediscoveryIncompleteRetryStopsAfterRecovery();
TestHidSessionRecoveryPolicy();
TestRediscoveryPassPrioritizesCreatedSessions();
TestHidCommandAttemptPolicy();
TestDeviceTransportPolicy();
TestNativeSettingsValidation();
TestCenturionFrameValidation();
TestCenturionConnectionNotificationDecode();
TestCenturionBridgeNotificationDecode();
TestCenturionFeatureMetadataDecode();
TestCenturionReadOnlyDeviceInfoDecode();
TestCenturionEndpointCandidatePolicy();
TestDiagnosticsPrivacyScope();
await TestDirectionalNamedPipeIpcAsync();
TestIpcSessionAuthentication();
await TestUpdaterDetachedSignatureVerificationAsync();
await TestUpdaterFileHashVerificationAsync();
TestUpdaterTrustedHosts();
TestEndpointReceiverIdentityValidation();
TestHidDeviceIndexCache();
TestPersistentReceiverIdentity();
TestSettingsFileStore();
TestCrashLogWriter();

Console.WriteLine("PowerTray.Tests passed.");
