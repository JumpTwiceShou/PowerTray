# PowerTray 1.5.0 Tooltip Copy, Width, And Content Correction

## Scope

- Increase only the tray-hover mode ComboBox by approximately two Simplified Chinese glyph widths.
- Replace the visible hover-mode note with the maintainer-requested Windows 11 custom-tooltip risk warning and avoidance choices.
- Remove charging/discharging/full/status wording from both native and custom tray hover text.
- Keep the version at `1.5.0`, run focused validation, and rebuild local unsigned installers from the final commit.

## Exclusions

- No coordinate, DPI, cursor, native-window movement, forced-open, or hover-timing changes.
- No real Logitech hardware, Shell, mixed-DPI, multi-monitor, or fullscreen acceptance.
- No candidate installation or replacement of the currently installed build.
- No remote push, tag, public release, asset publication, or device synchronization.
- Preserve the two unrelated pre-existing untracked task files.

## Ordered Work

- [x] Confirm current Git state, maintainer screenshots, width resource, localized copy, and tooltip text path.
- [x] Add the dedicated wider ComboBox resource and update all three language notes.
- [x] Restore battery tooltip content to device name, percentage, and optional voltage only.
- [x] Add focused regression assertions and run necessary Debug/Release validation.
- [x] Commit the exact 1.5.0 correction and rebuild local light/full installers.
- [x] Update design memory, archive this task, and report artifacts and unverified scope.

## Acceptance Criteria

- The tray-hover mode selector is 28 base DIPs wider than the ordinary 176-DIP settings ComboBoxes and follows app UI scaling.
- Simplified Chinese explicitly states that custom hover can appear at the Windows 11 top-left and fail to disappear, and recommends Windows native hover or disabling hover to avoid it.
- English and Japanese preserve the same warning and restart requirement.
- Neither native nor custom tray hover adds power-supply status wording.
- Existing tooltip mode exclusivity, restart application, disposal, and native 127-code-unit limits remain unchanged.

## Rollback

- Revert the scoped correction commit.
- Pre-correction 1.5.0 product source is `96f9ca913f61c2cfe0e6403bbb23f862ce7ff892`.

## Evidence

- Maintainer screenshots: `C:\Users\jiang\AppData\Local\Temp\codex-clipboard-ed882088-cef0-4f44-8364-60ca392d8b8a.png` and `C:\Users\jiang\AppData\Local\Temp\codex-clipboard-fb4e5eab-d3a0-4610-a515-0608fbf26cd4.png`.
- Current ordinary settings ComboBox width is `176.0 * scale`.
- Current tooltip detail was changed in 1.5.0 to append localized charging state; the maintainer rejected that addition.
- `UITrayToolTipModeComboWidth` is `204.0 * scale`; language and theme selectors remain `176.0 * scale`.
- English, Simplified Chinese, and Japanese now warn that PowerTray custom hover can remain at the Windows 11 top-left and recommend Windows native hover or disabling hover.
- `BuildBatteryToolTipDetail()` again emits only percentage, optional voltage, and Debug-only timestamp. All fifteen added `BatteryStatus*` catalog entries were removed.
- Focused assertions verify the 28-DIP base width difference, three-language warning semantics, absence of `BatteryStatus*` keys, and absence of charging text/status separators in production tooltip content.
- SDK 8.0.423 MSBuild Debug and Release solution builds passed with zero warnings and errors without changing `global.json`.
- Complete Debug and Release test programs passed after supported shutdown of installed `1.5.0+96f9ca9`; the same installed UI/helper were restarted afterward.
- Product correction commit: `e9ca0b7b3a363f89964611a821b418f66a359499`.
- Framework-dependent UI/HID assemblies report `FileVersion 1.5.0.0` and `ProductVersion 1.5.0+e9ca0b7b3a363f89964611a821b418f66a359499`.
- Local light installer: `bin/Release/powertray-1.5.0-e9ca0b7/installer/PowerTraySetup-local-1.5.0.exe`, 3,819,805 bytes, SHA-256 `F5A1D2CAF24F37ACD82767DEC565CC3FC5D073650E46D7416E687ACDA6A3700B`.
- Local full installer: `bin/Release/powertray-1.5.0-e9ca0b7/installer/PowerTraySetup-full-local-1.5.0.exe`, 51,600,440 bytes, SHA-256 `6B2D73456ACB9CB8952BD0EE4B97B3160E34F7B1992CFBFE3EA7A4AAC06AE7E4`.
- Matching `.sha256` files were generated; both local candidates are intentionally unsigned because this machine does not hold the pinned update-signing key.
- The corrected candidate was not installed. The running installed build remains `1.5.0+96f9ca9`; only its supported shutdown/restart path was used for isolated tests.
- Real Shell rendering, hardware, mixed-DPI, multi-monitor, and fullscreen acceptance remain explicitly unverified.
