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
- [ ] Back up and install the local diagnostic candidate.
- [ ] Capture and analyze one headset shutdown/startup cycle.

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
