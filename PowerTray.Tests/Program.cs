using LGSTrayCore;
using LGSTrayHID;
using LGSTrayHID.Features;
using LGSTrayUI;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using System.Text.Json;

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
        "endpoint-key",
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
    Assert(settings.UrlPrefix == "http://+:12321", "HTTP server should allow wildcard binding only when AllowRemote is explicit.");
}

static void TestTrayToolTipSeparators()
{
    Assert(LogiDeviceViewModel.FormatToolTipDetail("测试", "39.00%") == "测试，39.00%", "CJK tooltip separator should be a full-width comma without a space.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("G502", "39.00%") == "G502, 39.00%", "ASCII tooltip separator should be a comma plus a space.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("マウス", "39.00%") == "マウス，39.00%", "Japanese tooltip separator should be a full-width comma.");
    Assert(LogiDeviceViewModel.FormatToolTipDetail("Ｇ５０２", "39.00%") == "Ｇ５０２，39.00%", "Full-width tooltip separator should be a full-width comma.");
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

TestXmlEscaping();
TestBattery1F20Decode();
TestBattery1001LookupBoundaries();
TestNativeIdentityDiagnosticsRedaction();
TestUpdaterAssetSelectionAndChecksum();
TestHttpServerLoopbackFallback();
TestTrayToolTipSeparators();
await TestDeferredOfflineGateDelaysOffline();
await TestDeferredOfflineGateCancelsOffline();
TestDeferredOfflineGatePassesThroughOutsideGraceWindow();

Console.WriteLine("PowerTray.Tests passed.");
