# Centurion Status Notification

## Scope

- Implement immediate Centurion headset offline handling from the captured unsolicited bridge status notification.
- Recover online state by reading fresh device data rather than publishing cached battery state.
- Decode unsolicited bridge MessageEvent notifications and apply fresh battery/charging data immediately.
- Let addressed 0x50 sessions learn their address from the first valid RX frame and complete deferred discovery.
- Preserve Centurion feature type/version metadata and load firmware/hardware/model information into diagnostics without delaying initial device publication.
- Admit unknown Logitech Centurion candidates from the HID descriptor-derived `0xFFA0` usage page, then fail closed unless 0x50/0x51 protocol discovery succeeds.
- Keep the current presence probe as fallback.
- Compare the captured protocol shape with current public reference implementations and protocol documentation.
- Build and install a local-only 1.4.3 candidate for physical validation.

## Exclusions

- No headset setting reads or writes, including auto-sleep, sidetone, Mic SNR, EQ, RGB, mute, or gain controls.
- No multi-frame bridge write path whose only current consumers would be headset settings.
- No guessed handling for unknown states, payload sizes, bridge indexes, report ids, or device addresses.
- No fabricated battery or online state.
- No tooltip, theme, alert, updater, public release, push, or other-machine sync.
- No foreground desktop automation.

## Ordered Work

- [x] Inspect current source/runtime state and relevant public references.
- [x] Add a strict, testable Centurion status notification decoder.
- [x] Wire offline notification to immediate idempotent OFFLINE handling.
- [x] Wire online notification to fresh presence/discovery recovery.
- [x] Preserve active presence probing as fallback.
- [x] Run focused tests and Debug/Release builds.
- [x] Back up and install the local diagnostic candidate.
- [ ] Validate physical power-off and power-on behavior.
- [x] Decode MessageEvent and accept only strict unsolicited sub-device notifications.
- [x] Publish fresh battery/charging MessageEvent data and confirm other events through a fresh probe.
- [x] Complete deferred 0x50 initialization after learning an address from a valid first RX frame.
- [x] Preserve feature type/version metadata in diagnostics.
- [x] Load firmware/hardware/model information lazily into diagnostics.
- [x] Probe unknown `0xFFA0` Logitech Centurion candidates safely across report 0x51/0x50.
- [x] Run focused regression tests and rebuild the expanded candidate.
- [x] Back up and install the expanded candidate.
- [ ] Physically validate the expanded candidate with the PRO X 2 powered on and off.

## Acceptance Criteria

- Only the active Centurion report/address/bridge tuple and exact four-byte connection payload is accepted.
- A zero sub-device descriptor-list length reports the known headset offline immediately and idempotently.
- A non-zero sub-device descriptor-list length publishes online only after a fresh device response succeeds.
- Unknown or malformed frames continue through normal response handling and never change state.
- The helper stays running and the existing presence probe remains functional as fallback.
- Only MessageEvent frames for the active report/address/bridge, sub-device id zero, valid declared length, and software id zero are consumed as notifications.
- A valid `0x0104` MessageEvent publishes its own fresh battery data; other valid MessageEvents trigger fresh battery confirmation before online publication.
- An unknown `0xFFA0` endpoint never becomes a PowerTray device until Centurion root/feature discovery and a real device initialization succeed.
- Read-only metadata collection never blocks initial INIT/UPDATE and never writes a headset feature.

## Evidence

- Physical capture recorded unsolicited `[0x03, 0x00, 0x00, 0x01]` on power-on and `[0x03, 0x00, 0x00, 0x00]` on power-off.
- The existing active presence request did not start until 3,346.7 ms after the captured offline notification.
- Solaar `5e4ae7261eb922d7b8d8e0df73756c32eab32821` implements Centurion `ConnectionStateChangedEvent`: the bridge index is the notification sub-id, function `0` is the connection event, software id `0` identifies an unsolicited feature notification, and the first two data bytes encode the sub-device descriptor-list length (`0` disconnected, non-zero connected).
- Commit `2e59f04c9d4bda7cfb446929389cdfe584e10c84` passed complete Debug and Release builds and test programs with SDK 8.0.423.
- Local light installer `PowerTraySetup-centurion-status-1.4.3.exe` is 3,804,180 bytes with SHA-256 `25DB4BEE51EA6A6CA949C045C7D1B1B2098E12A929C456AC974DC2B426F4C655`.
- The previous 586-file `1.4.3+a4ea3df...` installation is backed up at `%TEMP%\PowerTray-1.4.3-before-centurion-status-20260725-0348`.
- Installed UI and helper binaries match the candidate publish exactly and report `1.4.3+2e59f04...`; health is running with zero helper restarts. Device count remained zero during the first 50-second validation window because the headset stayed powered off.
- The expanded implementation adds strict bridge MessageEvent decoding, immediate `0x0104` battery publication, first-RX report/address learning, serialized deferred discovery, feature type/version diagnostics, and delayed read-only `0x0100` hardware/firmware collection.
- Unknown Logitech endpoints enter Centurion probing only when HID enumeration reports an opened `0xFFA0` endpoint. Known product/report mappings remain preferred; unknown products negotiate `0x51` then addressed `0x50`, and create no PowerTray device until real feature/device discovery succeeds.
- Complete Debug and Release builds and both complete test-program runs passed with SDK 8.0.423 after the installed app was stopped through its supported `--shutdown` path.
- Product commit `3f8f537c18e6cb505f9f2232b8f319b286bf5c59` was published into the local-only light installer `PowerTraySetup-centurion-readonly-1.4.3.exe` (3,810,287 bytes, SHA-256 `6EC8D729C2BAEE20FF3BEF19A6EF4A3128F551C0DA6F48E6421D7FD7F8E0074A`).
- The prior 586-file `1.4.3+2e59f04...` installation is backed up at `%TEMP%\PowerTray-1.4.3-before-centurion-readonly-20260725-0510`. Silent installation exited `0`; installed UI/helper hashes match the candidate and both report `1.4.3+3f8f537...`, edition `light`.
- Post-install health stayed `running` with zero helper restarts and no matching Application Error/.NET Runtime crash. The device count changed from zero to two as the separate PRO X Wireless headset and PRO X2 SUPERSTRIKE mouse appeared; the PRO X 2 receiver continued returning sub-device unavailable because that headset remained powered off, so its new MessageEvent/metadata path still needs the physical power-cycle gate.
