using LGSTrayCore;
using LGSTrayHID;
using LGSTrayHID.Features;
using LGSTrayHID.HidApi;
using LGSTrayUI;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;
using LGSTrayPrimitives.IPC;
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
    Assert(settings.UrlPrefix == "http://localhost:12321", "HTTP server should remain loopback-only when no remote access token is configured.");

    settings.AccessToken = new string('x', 32);
    Assert(settings.UrlPrefix == "http://+:12321", "HTTP server should allow wildcard binding only with explicit remote mode and a strong token.");
    Assert(settings.IsAuthorized(new string('x', 32)), "The configured HTTP token should authorize remote requests.");
    Assert(!settings.IsAuthorized(new string('y', 32)), "An incorrect HTTP token should be rejected.");

    settings.Addr = "2001:db8::1";
    Assert(settings.UrlPrefix == "http://[2001:db8::1]:12321", "Explicit remote IPv6 addresses should be bracketed in URL prefixes.");
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

static void TestDeviceTransportPolicy()
{
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
TestDeviceTransportPolicy();
TestNativeSettingsValidation();
TestCenturionFrameValidation();
TestDiagnosticsPrivacyScope();
TestIpcSessionAuthentication();
await TestUpdaterDetachedSignatureVerificationAsync();
await TestUpdaterFileHashVerificationAsync();
TestUpdaterTrustedHosts();
TestEndpointReceiverIdentityValidation();
TestPersistentReceiverIdentity();

Console.WriteLine("PowerTray.Tests passed.");
