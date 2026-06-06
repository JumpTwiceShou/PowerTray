# PowerTray

**Language:** **English** | [简体中文](README.zh-CN.md)

---

<p align="center">
  🌐 <strong>Language / 语言</strong><br>
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

---

PowerTray is a vibe-driven modification and optimization based on [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery). It keeps the tray battery-monitoring idea, HTTP compatibility, and HID++ direction from the original project, while changing the app into a native-only Logitech battery tray tool that does not depend on the Logitech G Hub backend.

## 中文概要

PowerTray 是基于 [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) 进行 vibe 修改和优化的 native-only 罗技设备电量托盘工具。它不依赖 Logitech G Hub 后端，直接通过 HID++ 读取设备电量，支持托盘设备图标、低电量提醒、托盘闪烁、每设备别名和阈值、双语安装器，以及兼容的本地 HTTP API。

## Highlights

- Native Logitech HID++ battery reading through `hidapi`.
- No dependency on `lghub_agent.exe` or `ws://localhost:9010`.
- Tray icons for selected devices, including mouse/headset icons and numeric battery mode.
- Per-device low battery alerts with independent threshold, Windows notification, tray blinking, alias, and pause controls.
- Quiet hours and fullscreen-app notification suppression.
- Bilingual UI and installer: English and Simplified Chinese.
- Single-file Windows x64 installer with optional Start with Windows.
- Compatible local HTTP API for `/devices` and `/device/{id}` XML.

## Screenshots And Icon Demos

Some icon and API demo images are reused from the upstream `LGSTrayBattery` README with thanks.

### Tray Indicator

![Tray indicator](https://user-images.githubusercontent.com/24492062/138280300-6966b6a4-ff6d-46e6-9698-d2c8d612eb11.png)

Battery percentage and voltage, when supported, are shown from tray tooltips.

### Multiple Device Icons

![Multiple icons](Assets/multi_icon.png)

Selected devices can be shown as separate tray icons. When at least one device icon is selected, PowerTray hides the generic main tray icon.

### Numeric Battery Icon

![Numeric icon](Assets/numerical_icon.png)

Numeric mode displays the current battery percentage directly in the tray icon.

### Reactive Icons

![Device type icons](https://user-images.githubusercontent.com/24492062/138284660-95949372-c59a-4569-9545-0cfe0506d1fb.png)

Icons change by device type. Current UI assets support mouse, keyboard, and headset style icons.

![Theme reactive icons](https://user-images.githubusercontent.com/24492062/138285048-ad229703-5c4e-430e-b107-c50eb341e46b.png)

Icons react to the Windows light/dark theme.

![Charging icon](Assets/charging_icon.png)

Charging status is reflected when the device reports it through HID++.

### HTTP Server Demo

![HTTP server index](Assets/server_index.png)

The local HTTP server exposes a simple device list and XML battery endpoint.

![HTTP XML result](https://user-images.githubusercontent.com/24492062/138281030-f40ba805-69bf-48ac-a126-6f58f9ca7828.png)

## Current Device Coverage

The native backend has been validated on:

- `PRO X2 SUPERSTRIKE Wireless Mouse`
- `PRO X 2 Lightspeed Gaming Headset`

The native backend includes explicit headset recognition for G533, G535, G733, G935, and PRO X Wireless headset product ids. G733-style headsets use `0x1F20 ADC MEASUREMENT` battery data when available.

G522 LIGHTSPEED support is implemented for Centurion `0x50` transport and `0x0104` battery reads, but has not been validated with physical G522 hardware in this release.

Other Logitech HID++ devices may work if they expose supported battery features (`0x1000`, `0x1001`, `0x1004`, `0x1F20`) through compatible HID++ endpoints.

## Install

Download `PowerTraySetup.exe` from the [latest release](https://github.com/JumpTwiceShou/PowerTray/releases/latest) and run it. Use `PowerTraySetup-full.exe` only if you need the installer to include the .NET runtime.

During installation you can choose:

- English or Simplified Chinese initial language.
- Install location.
- Whether PowerTray starts with Windows.
- Whether PowerTray launches after installation.

User settings are stored at:

```text
%APPDATA%\PowerTray\settings.json
```

## Settings

The settings window includes:

- General: language, Start with Windows, numeric battery icon.
- Alerts: global low battery defaults, quiet hours, fullscreen notification suppression.
- Devices: per-device alias, low battery threshold, notification, tray blinking, alert pause, test notification, test blink.
- Diagnostics: G Hub process status, `localhost:9010` reachability, device update time, alert settings summary, and diagnostic export.

Device aliases only affect the UI and notifications. The HTTP XML API keeps the original Logitech device name.

## HTTP API

The local HTTP server defaults to:

```text
http://localhost:12321/
```

Endpoints:

- `GET /devices`: lists available devices and links.
- `GET /device/{deviceId}`: returns XML battery data.

Example XML:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<xml>
  <device_id>native-device-id</device_id>
  <device_name>Original Logitech Device Name</device_name>
  <device_type>Mouse</device_type>
  <battery_percent>86.00</battery_percent>
  <battery_voltage>0.00</battery_voltage>
  <mileage>-1.00</mileage>
  <charging>False</charging>
  <last_update>06/05/2026 22:28:44 +09:00</last_update>
</xml>
```

Native mode does not have G Hub mileage data, so `mileage` is reported as `-1.00`.

## Build

Use the bundled local SDK on this machine:

```powershell
F:\logi\.dotnet-sdk\dotnet.exe build PowerTray.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The installer is generated at:

```text
bin\Release\installer\PowerTraySetup.exe
```

Generated `bin`, `obj`, publish output, and installer payload zip files are not intended to be committed.

## License

PowerTray is licensed under GPL-3.0. See [LICENSE](LICENSE).

## Acknowledgements

Thanks to:

- [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery), the project this work is based on.
- [andyvorld/LGSTrayBattery_GHUB](https://github.com/andyvorld/LGSTrayBattery_GHUB), referenced by the upstream project.
- [Solaar](https://github.com/pwr-Solaar/Solaar), for HID++ protocol knowledge and reverse-engineering references acknowledged by the upstream project.
- [XB1ControllerBatteryIndicator](https://github.com/NiyaShy/XB1ControllerBatteryIndicator), for the icon idea and base acknowledged by the upstream project.
- [The Noun Project](https://thenounproject.com/), and the icon authors acknowledged by the upstream project: projecthayat, HideMaru, and Peter Lakenbrink.
- [hidapi](https://github.com/libusb/hidapi), for the HID library used by the native backend.
