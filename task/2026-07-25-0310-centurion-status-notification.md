# Centurion Status Notification

## Scope

- Implement immediate Centurion headset offline handling from the captured unsolicited bridge status notification.
- Recover online state by reading fresh device data rather than publishing cached battery state.
- Keep the current presence probe as fallback.
- Compare the captured protocol shape with current public reference implementations and protocol documentation.
- Build and install a local-only 1.4.3 candidate for physical validation.

## Exclusions

- No broad Centurion feature rewrite.
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

## Acceptance Criteria

- Only the active Centurion report/address/bridge tuple and exact four-byte connection payload is accepted.
- A zero sub-device descriptor-list length reports the known headset offline immediately and idempotently.
- A non-zero sub-device descriptor-list length publishes online only after a fresh device response succeeds.
- Unknown or malformed frames continue through normal response handling and never change state.
- The helper stays running and the existing presence probe remains functional as fallback.

## Evidence

- Physical capture recorded unsolicited `[0x03, 0x00, 0x00, 0x01]` on power-on and `[0x03, 0x00, 0x00, 0x00]` on power-off.
- The existing active presence request did not start until 3,346.7 ms after the captured offline notification.
- Solaar `5e4ae7261eb922d7b8d8e0df73756c32eab32821` implements Centurion `ConnectionStateChangedEvent`: the bridge index is the notification sub-id, function `0` is the connection event, software id `0` identifies an unsolicited feature notification, and the first two data bytes encode the sub-device descriptor-list length (`0` disconnected, non-zero connected).
- Commit `2e59f04c9d4bda7cfb446929389cdfe584e10c84` passed complete Debug and Release builds and test programs with SDK 8.0.423.
- Local light installer `PowerTraySetup-centurion-status-1.4.3.exe` is 3,804,180 bytes with SHA-256 `25DB4BEE51EA6A6CA949C045C7D1B1B2098E12A929C456AC974DC2B426F4C655`.
- The previous 586-file `1.4.3+a4ea3df...` installation is backed up at `%TEMP%\PowerTray-1.4.3-before-centurion-status-20260725-0348`.
- Installed UI and helper binaries match the candidate publish exactly and report `1.4.3+2e59f04...`; health is running with zero helper restarts. Device count remained zero during the first 50-second validation window because the headset stayed powered off.
