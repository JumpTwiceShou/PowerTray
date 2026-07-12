# Native hidapi dependency

PowerTray 1.4.2 ships a reproducible Windows x64 `hidapi.dll` built from the official [`libusb/hidapi`](https://github.com/libusb/hidapi) source snapshot at commit [`5360e03d6edcb7820eda3dd0fa1f8706e82e2600`](https://github.com/libusb/hidapi/commit/5360e03d6edcb7820eda3dd0fa1f8706e82e2600).

## Verified binary

- File: `hidapi.dll`
- SHA-256: `FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1`
- Architecture: Windows x64 PE32+
- Authenticode: unsigned
- Source baseline: [`hidapi-0.15.0`](https://github.com/libusb/hidapi/releases/tag/hidapi-0.15.0), commit `d6b2a974608dec3b76fb1e36c189f22b9cf3650c`
- Source archive SHA-256: `4DC06B08B90E07BA8D146847678792C454B78CB2B4E015AE88236891E5225048`
- Local patches: none

The selected snapshot contains the required connection-callback hotplug API and the related Windows unplug, timeout, concurrency, and callback-safety fixes. It preserves the two PowerTray-specific exports:

- `hid_hotplug_register_callback`
- `hid_hotplug_deregister_callback`

## Rebuild and verification

The immutable source manifest, required upstream commits, and clean-build script live under [`native/hidapi`](../../native/hidapi). Run the build script from the repository root to create an evidence-backed artifact, then run:

```powershell
.\LGSTrayHID\libhidapi\verify-hidapi.ps1
```

The verifier requires the pinned SHA-256, Windows x64 architecture, all 12 PowerTray-required exports, and an Authenticode status of `NotSigned` or `Valid`. CI runs the same verification and fails if the shipped binary changes.
