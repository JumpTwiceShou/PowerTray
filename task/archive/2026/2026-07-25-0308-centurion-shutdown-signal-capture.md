# Centurion Shutdown Signal Capture

## Scope

- Add an opt-in, bounded raw Centurion TX/RX trace for one physical headset power-cycle test.
- Keep the trace disabled unless a temporary environment variable supplies a new output path.
- Record enough timing and decoded payload data to distinguish unsolicited frames from request responses.
- Build and install a local-only 1.4.3 diagnostic candidate without touching the foreground desktop.

## Exclusions

- Do not treat an unknown frame as a shutdown signal.
- Do not change the current 15-second presence probe or offline thresholds.
- Do not fabricate device state or treat an intentionally disconnected device count as a bug.
- Do not change tooltip, theme, alert, updater, public release, remote branches, or other machines.
- Do not expose raw trace contents publicly or commit a captured trace.

## Ordered Work

- [x] Confirm the existing reader receives frames continuously but only parses standard HID++ connection notifications asynchronously.
- [x] Confirm Centurion frames are otherwise consumed only inside request-response windows.
- [x] Add an opt-in bounded Centurion trace.
- [x] Run focused build/test validation.
- [x] Back up and install the local diagnostic candidate.
- [x] Capture and analyze one headset shutdown/startup cycle.

## Acceptance Criteria

- Normal launches without the trace environment variable create no trace file.
- A trace-enabled launch records Centurion TX and RX with timestamps and decoded payloads.
- Trace output is bounded and local-only.
- The candidate does not change online/offline behavior before a signal is understood.
- The physical capture distinguishes one of:
  - a repeatable unsolicited shutdown frame;
  - only failed active probe responses;
  - inconclusive/noise requiring another narrowly scoped capture.

## Evidence

- `HidppDevices.ReadLoop` continuously forwards incoming reports to `ProcessMessage`.
- `ProcessMessage` directly recognizes only standard HID++ report `0x10`, notification `0x41`.
- Centurion requests read from the shared response channel only after a command is sent and discard non-matching payloads.
- `POWERTRAY_CENTURION_TRACE_PATH` now opts one helper process into a new-file-only JSONL trace capped at 8 MiB.
- Each trace record contains UTC and monotonic elapsed time, direction, product id, endpoint path hash, raw frame, and decoded Centurion report/address/payload when valid.
- SDK 8.0.423 MSBuild Debug and Release solution builds completed with zero warnings/errors.
- After a supported background `PowerTray.exe --shutdown`, the complete Debug `PowerTray.Tests` program passed.
- Local diagnostic source commit is `a4ea3df83da0dae91e636262faf979cdda28ff3b`.
- Built the local-only light installer at `bin/Release/centurion-trace-candidate-20260725-0315/installer/PowerTraySetup-centurion-trace-1.4.3.exe`: 3,802,876 bytes, SHA-256 `12AD45B466D590D50FDD010A995846D4C051C6AC095ED3FF1F26F14E1F236ACD`.
- Verified the published `hidapi.dll` as Windows x64 with the pinned SHA-256 and all 12 required exports.
- Backed up all 586 files from the prior `31cce6c` installation to `%TEMP%\PowerTray-1.4.3-before-centurion-trace-20260725-0317`; helper version and SHA-256 match the source installation.
- Silent overwrite installed exact publish hashes. UI/helper report `1.4.3+a4ea3df83da0dae91e636262faf979cdda28ff3b`, remain responsive, and `/health` reports running with zero restarts.
- Trace-enabled launch created `%TEMP%\PowerTray-centurion-trace-20260725-0318.jsonl`. Initial startup recorded product `0x0AF7` traffic in both directions, proving the diagnostic path is active.
- Physical power-on produced unsolicited RX payload `03 00 00 01` at `2026-07-25 02:59:07.825 +09:00`.
- Physical power-off produced unsolicited RX payload `03 00 00 00` at `2026-07-25 02:59:14.621 +09:00`.
- Neither status payload had a corresponding outgoing request. Byte `0x03` matches the discovered Centurion bridge feature index; function/software-id byte `0x00` distinguishes the message from command responses.
- The first existing active battery-presence request started 3,346.7 ms after the unsolicited offline payload and received `FF 03 1D 0B`; the next two confirmation attempts received the same unavailable error with their own software ids.
- This establishes a high-confidence immediate online/offline signal for the physical PRO X 2 headset. A production parser should require the exact bridge index, unsolicited function byte, two-byte state body, and known state values before changing device state.
- Closed the 206,933-byte trace after capture; SHA-256 is `F0821A5A41B632B9EFEC5DFEE14B94D57679B5A84B914FE0C6AE1A472EC42FE9`.
- Restarted the installed candidate without the trace environment variable. The trace remained unchanged; UI/helper are responsive, `/health` reports running, two intentionally online devices, and zero restarts.

## Result

- The headset/receiver does send a usable Centurion bridge status notification.
- PowerTray previously received but did not parse this unsolicited message.
- Observed state mapping is `03 00 00 01` for online and `03 00 00 00` for offline on bridge index `0x03`.
- No production state rule was added in this diagnostic task. The next implementation should parse the signal conservatively and keep active probing as fallback.
