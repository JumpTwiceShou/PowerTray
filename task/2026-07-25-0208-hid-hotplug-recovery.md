# HID Hotplug And First-Discovery Recovery

## Scope

- Fix lost rediscovery requests when Logitech USB/HID endpoints change while a discovery pass is already running.
- Add a bounded, last-arrival-relative retry window for newly arriving or still-empty HID sessions.
- Make device-slot initialization awaitable and deduplicated so manual rediscovery has meaningful completion semantics.
- Retry unknown LIGHTSPEED device slots without rebuilding healthy sessions or restarting `PowerTrayHID`.
- Confirm direct USB removal promptly while retaining the existing receiver-flap protection.
- Build and install a local-only 1.4.3 light candidate for physical USB/LIGHTSPEED validation.

## Exclusions

- No periodic full HID rediscovery loop.
- No fabricated devices, battery values, or changes to the meaning of `deviceCount`.
- No device-name or C094-only behavior.
- No tooltip, theme, alert, update, release, or fullscreen behavior changes.
- No push, tag, GitHub Release, public asset, or other-machine installation.
- No desktop UI automation.

## Ordered Work

- [x] Capture the physical C094 USB timeline and identify the retry/lost-wakeup failure path.
- [x] Implement and test lossless rediscovery scheduling.
- [x] Implement and test bounded arrival/empty-session recovery.
- [x] Make device initialization awaitable and manual rediscovery deterministic.
- [x] Add targeted unknown-slot LIGHTSPEED recovery.
- [x] Add direct-device removal confirmation without weakening receiver protection.
- [x] Run focused tests and Debug/Release builds.
- [x] Commit the exact candidate source.
- [x] Build, back up, install, and verify the local 1.4.3 light candidate.
- [ ] Complete physical USB/LIGHTSPEED validation with maintainer actions.

## Acceptance Criteria

- A rediscovery request arriving during an active pass always causes a later pass.
- A composite USB device that stabilizes after the first second is discovered within the bounded retry window.
- A session with valid endpoints but zero initialized devices is retried without rebuilding healthy sessions.
- Manual rediscovery does not return before queued device initialization completes or fails.
- An unknown LIGHTSPEED slot can initialize after its online notification without a full helper restart.
- A confirmed direct USB endpoint removal becomes offline promptly, while receiver-path transient removal remains protected.
- Existing responsive sessions, device identities, tooltip behavior, and battery semantics remain unchanged.

## Physical Evidence

- During the 2026-07-25 monitor, Windows exposed C094 direct USB/HID nodes with serial `AAA4F2DA` and valid HID++ `FF00:0001` and `FF00:0002` endpoints.
- The stable C094 arrival was observed at `01:57:58.258 +09:00`, but PowerTray remained at two devices through the end of the 75-second monitor.
- `PowerTrayHID` later started a new process at `02:01:03`; C094 appeared as `PRO X Wireless` at `02:01:11.441`, demonstrating that stable startup discovery succeeds while the hotplug recovery path does not.
- The current scheduling code can attempt to queue a follow-up before `_rediscoverQueued` is cleared, leaving `_rediscoverRequestedWhileRunning` set with no remaining consumer.
- Presence recovery only schedules discovery when the total session count is zero, so one empty C094 session is masked by other healthy sessions.

## Result

- Added a cancellable rediscovery scheduler whose arrival window is rebased to the latest hotplug event and runs at event-relative offsets `0/300/1000/2500/5000ms`.
- Arrival recovery retains all bounded attempts even when an early enumeration is empty or transiently fails; later attempts enumerate endpoints again while refreshing only incomplete sessions.
- Discovery requests now wait behind the active rediscovery lock instead of setting a follow-up flag that can lose its consumer.
- Session device initialization is deduplicated and awaitable. Manual rediscovery performs one complete pass and does not respond until initialization succeeds or reaches a recorded failure.
- Unknown online device indexes receive their own event-relative bounded initialization retry without rebuilding healthy sessions.
- Presence recovery targets only sessions that still have no known device.
- Confirmed direct-device endpoint removal promotes a previously deferred offline signal without duplicating it; sessions that have detected receiver transport keep the existing three-second grace period.
- Debug and Release solution builds completed with zero warnings and zero errors. The complete Debug and Release `PowerTray.Tests` programs passed.
- The exact product/test candidate source is commit `0c7b35b58df743aa300e7eea441d7183401a1c5c` on local `main`.
- Built the local-only light installer at `bin/Release/hid-hotplug-recovery-candidate-20260725-0225/installer/PowerTraySetup-hid-hotplug-recovery-1.4.3.exe`: 3,800,980 bytes, SHA-256 `25C2E908E4449C46A0122BE33A4A602D2323D0586A5659C065CD92795B5C5304`.
- The release signing key is not present on this machine, so this local-only candidate has no public update checksum signature and was not prepared for distribution.
- Backed up the complete previous 586-file installation to `%TEMP%\PowerTray-1.4.3-before-hid-hotplug-recovery-20260725-0228`; its UI/HID assemblies remain `1.4.3+a7b488c46097fe0aa826244c244a0a2b885f3209` and both backup hashes match the source installation.
- Silent installation exited with code 0. All six checked installed files match the publish-directory hashes; UI and HID report `1.4.3+0c7b35b58df743aa300e7eea441d7183401a1c5c`, Hardcodet remains 2.0.1, hidapi remains the verified pinned binary, and the installed edition is `light`.
- The installed UI and helper are responsive with stable process IDs. `/health` reports `running`, `restartCount: 0`, and the exact candidate version; no matching PowerTray application crash event was recorded after startup.
- During the first 55-second physical observation, the live list recorded repeated `PRO X Wireless` disappearance/recovery at `02:29:24.319 -> 02:29:30.071` and `02:29:33.200 -> 02:29:47.820`. `PowerTrayHID` kept PID 29472 and `restartCount: 0` throughout, proving recovery no longer depends on restarting the helper.
- The LIGHTSPEED mouse also returned from idle during the same runtime without a helper restart. Final maintainer confirmation of tray-icon timing and a dedicated PRO X Wireless LIGHTSPEED-first-connect cycle remain pending.
