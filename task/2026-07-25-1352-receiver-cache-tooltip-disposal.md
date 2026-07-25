# Receiver Cache And Tooltip Disposal

## Scope

- Reduce same-process C54D receiver replug latency by reusing protocol-confirmed pairing slots.
- Reuse the stable endpoint-key and process-local index-cache infrastructure while keeping direct and receiver discovery semantics separate.
- Close and detach a device's custom WPF tray tooltip before its `TaskbarIcon` is disposed.
- Build and install a local-only 1.4.3 candidate for physical validation.

## Confirmed Evidence

- During the physical test, Windows reported C54D arrival at `2026-07-25 13:38:41.262 +09:00`; the first published mouse battery update was `13:38:46.478`, about 5.22 seconds later.
- The same-process C094 direct replug arrived at `13:39:03.194` and published at `13:39:03.565`, about 0.37 seconds later.
- The C54D delay aligns with the current bounded arrival fallback and still uses cold receiver-slot discovery.
- The maintainer reproduced the top-left orphan by hovering a device battery icon while unplugging that mouse.
- Hardcodet 2.0.1 opens `TrayToolTipResolved` on the Shell show event, but its `TaskbarIcon.Dispose(bool)` removes the icon/message sink without closing or clearing the resolved tooltip.
- PowerTray currently disposes the device `TaskbarIcon` immediately when the selected device becomes offline.

## Safety Boundaries

- Receiver cache entries are accepted only after full HID++ initialization on a session that confirmed receiver transport.
- Receiver values are a set of slots `1-6`; they never enter the direct `0x00`/`0xFF` path.
- A cached receiver-slot miss falls back to receiver-register discovery, complete slot probing, and the existing bounded arrival retries.
- Cached receiver success must not terminate discovery of other receiver slots.
- The optimistic cached-slot probe may bypass C54D recovery once for 150 ms; every normal C54D command retains the existing 600 ms timeout, two attempts, and long-report fallback.
- Tooltip cleanup occurs only during icon disposal. Do not add placement coordinates, DPI conversion, cursor polling, native window movement, opacity gating during normal hover, delayed opening, or a general close timer.
- Do not change Centurion behavior, battery semantics, device-count semantics, versions, public releases, remotes, or other machines.
- Preserve the two unrelated untracked tooltip task files.

## Ordered Work

- [x] Capture and compare C54D and C094 physical arrival/publication timestamps.
- [x] Confirm the tooltip orphan reproduction and upstream disposal gap.
- [x] Implement shared direct/receiver index-cache infrastructure.
- [x] Add optimistic cached receiver-slot probing with full fallback.
- [x] Close and detach custom tooltips before device-icon disposal.
- [x] Add focused cache, recovery-policy, and tooltip-disposal tests.
- [x] Run complete Debug and Release builds/tests serially.
- [x] Commit, package, back up, install, and smoke-test the local candidate.
- [ ] Complete maintainer receiver-replug and hover-unplug validation.

## Verification Evidence

- `HidDeviceIndexCache` keeps direct indexes and receiver slots in mutually exclusive, stable endpoint-keyed process-local state. Receiver slots are restricted to `1-6`, returned in deterministic order, and remembered only after full device initialization.
- Cached receiver slots receive one 150 ms short-report probe without C54D long-report recovery. A miss continues through receiver-register discovery, the full receiver slot scan, and the existing bounded arrival retries; a hit does not stop discovery of other slots.
- Device icon disposal now closes the resolved WPF tooltip, clears its content and device data context, detaches it from `TaskbarIcon`, and only then disposes the icon. No placement, DPI, cursor, native-window, delayed-open, opacity, fullscreen, or general-timer behavior changed.
- SDK `8.0.423` Debug and Release solution builds completed with zero warnings and errors.
- Debug and Release `PowerTray.Tests` both passed after the installed runtime exited through its supported background `--shutdown` path.
- `git diff --check` reported no whitespace errors.
- Product/task commit: `9ce30bce9c9a63d08949f5f564d20e4da9c6772a` (`fix: accelerate receiver replug and close orphan tooltips`).
- The unsigned local-only light installer is `bin/Release/receiver-cache-tooltip-disposal-9ce30bc/installer/PowerTraySetup-receiver-cache-tooltip-disposal-1.4.3.exe`: 3,812,704 bytes, SHA-256 `8F63A2A3B818B4AE24A551DD7577991114CD950CD4DBC5686374026C600FA803`.
- The former 587-file `1.4.3+9bba504...` installation is recoverable at `%TEMP%\PowerTray-1.4.3-before-receiver-cache-tooltip-disposal-20260725-140106`; source and backup file counts matched and all 587 file hashes matched.
- Silent installation exited `0`. Installed `PowerTray.dll`, `PowerTrayHID.dll`, and `hidapi.dll` hashes exactly match the candidate; UI/helper report `1.4.3+9ce30bc...`, edition remains `light`, and autostart remains disabled.
- The installed UI/helper are running, `/health` reports `running`, `restartCount=0`, and the currently powered PRO X2 SUPERSTRIKE mouse is published. No matching post-start Application Error, .NET Runtime, or Windows Error Reporting crash event was recorded.
- No remote, other machine, public tag, release, or asset changed.
