using LGSTrayCore;
using LGSTrayHID.Features;
using LGSTrayPrimitives;

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

TestXmlEscaping();
TestBattery1F20Decode();

Console.WriteLine("PowerTray.Tests passed.");
