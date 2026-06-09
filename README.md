# PowerTray

**Language:** **English** | [简体中文](README.zh-CN.md)

---

<p align="center">
  🌐 <strong>Language / 语言</strong><br>
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

---

PowerTray is a native-only Logitech battery tray app based on [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery). It keeps the original tray battery-monitoring concept, HTTP compatibility, and HID++ direction, while removing the dependency on the Logitech G Hub backend.

## 中文概要

PowerTray 是基于 [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) 改造的罗技设备电量托盘工具。它不依赖 Logitech G Hub 后端，直接通过 HID++ 读取设备电量，支持托盘设备图标、低电量提醒、托盘闪烁、每设备自定义名称和阈值、双语安装器，以及兼容的本地 HTTP API。

## Highlights

- Native Logitech HID++ battery reading through `hidapi`.
- No dependency on `lghub_agent.exe` or `ws://localhost:9010`.
- Tray icons for selected devices, including mouse/headset icons and numeric battery mode.
- Per-device low battery alerts with independent threshold, Windows notification, tray blinking, alias, and pause controls.
- Quiet hours and notification suppression while full-screen apps are active.
- Bilingual UI and installer: English and Simplified Chinese.
- Windows x64 installer with optional Start with Windows integration.
- Compatible local HTTP API for `/devices` and `/device/{id}` XML.

## Screenshots and Icon Demos

Some icon and API demo images are reused from the upstream `LGSTrayBattery` README with thanks.

### Tray Indicator

![Tray indicator](https://user-images.githubusercontent.com/24492062/138280300-6966b6a4-ff6d-46e6-9698-d2c8d612eb11.png)

Tray tooltips show battery percentage and voltage when supported.

### Multiple Device Icons

![Multiple icons](Assets/multi_icon.png)

Selected devices can be shown as separate tray icons. When at least one device icon is selected, PowerTray hides the generic main tray icon.

### Numeric Battery Icon

![Numeric icon](Assets/numerical_icon.png)

Numeric mode displays the current battery percentage directly in the tray icon.

### Reactive Icons

![Device type icons](https://user-images.githubusercontent.com/24492062/138284660-95949372-c59a-4569-9545-0cfe0506d1fb.png)

Icons change by device type. The current UI assets support mouse, keyboard, and headset-style icons.

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

| Device | Status | Notes |
| --- | --- | --- |
| `PRO X2 SUPERSTRIKE Wireless Mouse` | Validated | Native HID++ battery reads through a LIGHTSPEED receiver. |
| `PRO X 2 Lightspeed Gaming Headset` | Validated | Native headset battery reads. |
| G533 / G535 / G733 / G935 / PRO X Wireless headsets | Recognized by product id | Expected to work only when a compatible HID++ battery feature is exposed. |
| G522 LIGHTSPEED | Implemented, not physically validated | Centurion `0x50` transport and `0x0104` battery reads are implemented. |

The native backend includes explicit headset recognition for G533, G535, G733, G935, and PRO X Wireless headset product ids. G733-style headsets use `0x1F20 ADC MEASUREMENT` battery data when available.

G522 LIGHTSPEED support is implemented for Centurion `0x50` transport and `0x0104` battery reads, but has not been validated with physical G522 hardware in this release.

Other Logitech HID++ devices may work if they expose supported battery features (`0x1000`, `0x1001`, `0x1004`, `0x1F20`) through compatible HID++ endpoints.

## Scope and Limitations

PowerTray is a lightweight battery/status tray utility. It does not replace Logitech G Hub for button mapping, profiles, macros, lighting, firmware updates, Dolby/Atmos settings, or other device configuration features.

PowerTray does not require the Logitech G Hub backend and does not modify Logitech drivers or device configuration. Device support depends on whether Windows exposes a compatible Logitech HID++ endpoint and whether the device reports one of the supported battery features.

## Privacy and Local-Only Behavior

PowerTray reads local HID++ battery data and stores user settings under `%APPDATA%\PowerTray`. The HTTP API is intended for local use and defaults to `localhost`. PowerTray does not collect telemetry; if automatic update checks are enabled, it contacts the GitHub Releases API for this repository.

## Install

Download `PowerTraySetup.exe` from the [latest release](https://github.com/JumpTwiceShou/PowerTray/releases/latest) and run it. Use `PowerTraySetup-full.exe` only if you need the installer to include the .NET runtime.

During installation you can choose:

- English or Simplified Chinese initial language.
- Install location.
- Whether PowerTray starts with Windows.
- Whether PowerTray checks for updates automatically.
- Whether PowerTray launches after installation.

User settings are stored at:

```text
%APPDATA%\PowerTray\settings.json
```

## Settings

The settings window includes:

- General: language, Start with Windows, automatic update checks, numeric battery icon.
- Alerts: default low-battery alert settings, quiet hours, full-screen notification suppression.
- Devices: custom device name, low-battery threshold, notifications, tray blinking, alert pause, test notification, test tray blink.
- Diagnostics: G Hub process status, `localhost:9010` reachability, last device update time, alert settings summary, and diagnostic export.

Device aliases only affect the UI and notifications. The HTTP XML API keeps the original Logitech device name.

## Troubleshooting

- If no supported devices appear, reconnect the receiver or device, then choose **Rescan devices** from the tray menu.
- If a headset such as G733 does not show battery data, the device may not expose a compatible HID++ battery feature on this Windows HID endpoint.
- If the lightweight installer reports that .NET 8 Desktop Runtime is missing, install the runtime first or use `PowerTraySetup-full.exe`.
- If the HTTP API is not reachable, check whether another local process is already using port `12321`.
- G Hub is not required. If G Hub is installed, PowerTray still reads battery data through its native backend by default and does not change Logitech driver or profile settings.

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

Native mode does not expose G Hub mileage data, so `mileage` is reported as `-1.00`.

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
