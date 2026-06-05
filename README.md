# PowerTray

PowerTray is a vibe-driven modification and optimization based on [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery). It keeps the tray battery-monitoring idea, HTTP compatibility, and HID++ direction from the original project, while changing the app into a native-only Logitech battery tray tool that does not depend on the Logitech G Hub backend.

PowerTray 是基于 [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) 进行 vibe 修改和优化的项目。它保留了原项目的托盘电量监控思路、HTTP 兼容接口和 HID++ 方向，但改造成不依赖 Logitech G Hub 后端的 native-only 罗技设备电量托盘工具。

## Highlights

- Native Logitech HID++ battery reading through `hidapi`.
- No dependency on `lghub_agent.exe` or `ws://localhost:9010`.
- Tray icons for selected devices, including mouse/headset device icons and numeric battery mode.
- Per-device low battery alerts with independent threshold, Windows notification, tray blinking, alias, and pause controls.
- Quiet hours and fullscreen-app notification suppression.
- Bilingual UI and installer: English and Simplified Chinese.
- Single-file Windows installer with optional Start with Windows.
- Compatible local HTTP API for `/devices` and `/device/{id}` XML.

## 功能亮点

- 通过 `hidapi` 直接读取 Logitech HID++ 电量。
- 不依赖 `lghub_agent.exe` 或 `ws://localhost:9010`。
- 可为已选择设备显示独立托盘图标，支持鼠标/耳机图标和数字电量图标。
- 每个设备可单独设置低电量阈值、Windows 通知、托盘闪烁、别名和暂停提醒。
- 支持静音时段，以及当前台存在全屏软件时暂停 Windows 通知。
- 应用和安装器支持英文/简体中文，默认英文。
- 提供单 EXE Windows 安装器，可选择是否开机自启。
- 保留兼容 HTTP API：`/devices` 和 `/device/{id}` XML。

## Current Device Coverage

The native backend has been validated on:

- `PRO X2 SUPERSTRIKE Wireless Mouse`
- `PRO X 2 Lightspeed Gaming Headset`

Other Logitech HID++ devices may work if they expose supported battery features (`0x1000`, `0x1001`, `0x1004`) through compatible HID++ endpoints.

## 当前设备覆盖

native 后端已验证：

- `PRO X2 SUPERSTRIKE Wireless Mouse`
- `PRO X 2 Lightspeed Gaming Headset`

其他 Logitech HID++ 设备如果通过兼容 HID++ endpoint 暴露受支持的电量 feature（`0x1000`、`0x1001`、`0x1004`），也可能可用。

## Install

Download `PowerTraySetup.exe` from the release page and run it. The installer includes all runtime dependencies for Windows x64.

During installation you can choose:

- English or Simplified Chinese initial language.
- Install location.
- Whether PowerTray starts with Windows.
- Whether PowerTray launches after installation.

User settings are stored at:

```text
%APPDATA%\PowerTray\settings.json
```

## 安装

从 release 页面下载 `PowerTraySetup.exe` 并运行。安装器自带 Windows x64 所需运行时依赖。

安装时可以选择：

- 初始语言：English 或简体中文。
- 安装位置。
- 是否开机自启。
- 安装完成后是否立即启动 PowerTray。

用户设置保存于：

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

## 设置

设置窗口包含：

- 常规：语言、开机自启、数字电量图标。
- 提醒：全局低电量默认值、静音时段、全屏软件前台时暂停 Windows 通知。
- 设备：每设备别名、低电量阈值、通知、托盘闪烁、暂停提醒、测试通知、测试闪烁。
- 诊断：G Hub 进程状态、`localhost:9010` 可达性、设备更新时间、提醒配置摘要和诊断导出。

设备别名只影响界面和通知。HTTP XML API 仍输出罗技原始设备名。

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

## HTTP API

本地 HTTP 服务默认地址：

```text
http://localhost:12321/
```

接口：

- `GET /devices`：列出可用设备和链接。
- `GET /device/{deviceId}`：返回 XML 电量数据。

Native 模式没有 G Hub 的 mileage 数据，因此 `mileage` 返回 `-1.00`。

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

## 构建

使用本机 bundled SDK：

```powershell
F:\logi\.dotnet-sdk\dotnet.exe build PowerTray.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

安装器输出：

```text
bin\Release\installer\PowerTraySetup.exe
```

生成的 `bin`、`obj`、publish 输出和安装器 payload zip 不应提交到仓库。

## License

PowerTray is licensed under GPL-3.0. See [LICENSE](LICENSE).

## 许可证

PowerTray 使用 GPL-3.0 许可证。见 [LICENSE](LICENSE)。

## Acknowledgements

Thanks to:

- [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery), the project this work is based on.
- [andyvorld/LGSTrayBattery_GHUB](https://github.com/andyvorld/LGSTrayBattery_GHUB), referenced by the upstream project.
- [Solaar](https://github.com/pwr-Solaar/Solaar), for HID++ protocol knowledge and reverse-engineering references acknowledged by the upstream project.
- [XB1ControllerBatteryIndicator](https://github.com/NiyaShy/XB1ControllerBatteryIndicator), for the icon idea and base acknowledged by the upstream project.
- [The Noun Project](https://thenounproject.com/), and the icon authors acknowledged by the upstream project: projecthayat, HideMaru, and Peter Lakenbrink.
- [hidapi](https://github.com/libusb/hidapi), for the HID library used by the native backend.

## 致谢

感谢：

- [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery)，本项目参考和修改优化的基础项目。
- [andyvorld/LGSTrayBattery_GHUB](https://github.com/andyvorld/LGSTrayBattery_GHUB)，上游项目引用的相关项目。
- [Solaar](https://github.com/pwr-Solaar/Solaar)，上游项目致谢其 HID++ 协议资料和逆向参考。
- [XB1ControllerBatteryIndicator](https://github.com/NiyaShy/XB1ControllerBatteryIndicator)，上游项目致谢其图标思路和基础。
- [The Noun Project](https://thenounproject.com/) 以及上游项目致谢的图标作者 projecthayat、HideMaru、Peter Lakenbrink。
- [hidapi](https://github.com/libusb/hidapi)，native 后端使用的 HID 库。
