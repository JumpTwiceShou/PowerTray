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
- [ ] Package, back up, and install the local 1.4.3 candidate.
- [ ] Measure headset power-off disappearance and power-on recovery.

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
- The exact product/test candidate source was committed on local `main`. Packaging, local installation, and physical headset timing validation remain pending.
