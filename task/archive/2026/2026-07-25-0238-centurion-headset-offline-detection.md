# Centurion Headset Offline Detection

## Scope

- Make a powered-off Centurion headset become offline without waiting for the ten-minute battery poll.
- Reuse the lightweight native presence check and confirm consecutive failures within one check.
- Publish the existing device identity and fresh battery state immediately when the headset becomes reachable again.
- Build and install a local-only 1.4.3 candidate for physical validation.

## Exclusions

- No fabricated device, battery, or online state.
- No change to USB endpoint-removal or receiver hotplug grace behavior.
- No helper restart as a discovery or offline mechanism.
- No tooltip, theme, alert, updater, public release, push, or other-machine installation.
- No desktop UI automation.

## Ordered Work

- [x] Confirm the Centurion headset is absent from lightweight presence probing.
- [x] Confirm the remaining path polls battery every 600 seconds and requires three failures.
- [x] Add Centurion presence probing and same-check failure confirmation.
- [x] Set the default lightweight presence interval to 15 seconds.
- [x] Add regression coverage and run Debug/Release validation.
- [x] Commit the exact local 1.4.3 candidate source.
- [x] Package, back up, and install the local 1.4.3 candidate.
- [x] Measure headset power-off disappearance and power-on recovery.

## Acceptance Criteria

- A reachable Centurion headset needs only one request during each presence check.
- A powered-off headset is reported offline only after the configured consecutive failures.
- Those failures are confirmed in one presence check rather than across multiple 60-second checks.
- A recovered headset republishes its current battery and becomes online without restarting `PowerTrayHID`.
- Existing standard HID++ devices, receiver protection, device identities, and battery semantics remain unchanged.

## Evidence

- Installed configuration currently uses `pollPeriod = 600`, `presencePeriod = 60`, and `consecutiveFailureThreshold = 3`.
- `HidppDevices.ProbePresenceAsync` currently iterates only `_deviceCollection`, while Centurion initialization registers its identity outside that collection.
- Centurion offline detection therefore falls back to `UpdateCenturionBatteryAsync` in the 600-second poll loop.

## Result

- Centurion initialization now retains an immutable battery-presence target for headsets that expose feature `0x0104`.
- Every native presence check probes a reachable Centurion headset once. A failure is retried up to the configured transport threshold within the same check before signalling offline.
- A successful probe clears the Centurion failure/offline state; recovery republishes the fresh battery update so the existing device becomes online without changing identity.
- Standard HID++ devices use the same within-check confirmation count, while their existing endpoint hotplug and offline-deferral behavior is unchanged.
- The default lightweight presence interval is now 15 seconds. The 600-second full battery publication interval remains unchanged.
- Debug and Release solution builds completed with zero warnings and zero errors. The complete Debug and Release `PowerTray.Tests` programs passed.
- The exact product/test candidate source is commit `31cce6c6ed0e3f9b2863535bf18d019ca95d832a` on local `main`.
- Built the local-only light installer at `bin/Release/centurion-offline-candidate-20260725-0237/installer/PowerTraySetup-centurion-offline-1.4.3.exe`: 3,801,357 bytes, SHA-256 `957BB832A02BEBB2F284082156545D5C0C5938F84C507A4621503127DAD62D68`.
- The release signing key remains absent on this machine, so the local-only installer is unsigned and was not prepared for distribution.
- Backed up the complete previous 586-file `0c7b35b` candidate to `%TEMP%\PowerTray-1.4.3-before-centurion-offline-20260725-0239`; UI and HID hashes match the source installation.
- Silent installation exited with code 0. Installed UI/HID assemblies and `appsettings.toml` match the final publish directory, report `1.4.3+31cce6c6ed0e3f9b2863535bf18d019ca95d832a`, use `presencePeriod = 15`, and remain the light edition.
- Physical monitoring recorded the Centurion headset offline at `02:39:41.097`, online at `02:40:02.602`, offline at `02:40:22.601`, and online again at `02:40:32.441`.
- `PowerTray` PID 11596 and `PowerTrayHID` PID 33576 remained unchanged through both transitions; `/health` kept `restartCount: 0`. Power-off disappearance and power-on recovery no longer depend on the 600-second battery poll or a helper restart.
