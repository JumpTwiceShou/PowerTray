This release fixes native HID++ discovery stability for C54D LIGHTSPEED receivers and improves startup ordering across Logitech HID sessions.

## Highlights

- Fixed PRO X2 SUPERSTRIKE Wireless Mouse sometimes not appearing in native-only mode even though the receiver and device were connected.
- Added retry and recovery handling for intermittent C54D HID++ write failures during device feature discovery.
- Added a long-report fallback path for C54D HID++ 2.0 requests when short-report reads time out.
- Serialized native Logitech HID session startup to reduce discovery races between receivers and headset endpoints.
- Improved native diagnostics events for HID write failures, read timeouts, and endpoint reopen recovery.

## Validated devices

- PRO X2 SUPERSTRIKE Wireless Mouse
- PRO X 2 Lightspeed Gaming Headset

## Downloads

- `PowerTraySetup.exe`: lightweight installer, requires .NET 8 runtime components.
- `PowerTraySetup-full.exe`: full installer with runtime included.
