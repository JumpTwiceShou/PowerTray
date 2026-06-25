## What's New

- Fixed devices being marked offline after transient Logitech HID hotplug/re-enumeration, such as connecting a headset USB charging cable.
- Deferred short-lived OFFLINE events during the hotplug grace window and cancelled them when the device reports a fresh INIT or UPDATE.
- Improved rediscovery reliability by queuing a follow-up rediscover when another rediscover request arrives while discovery is already running.
- Added regression tests for deferred OFFLINE emission, cancellation, and pass-through behavior outside the grace window.

## Notes

- This is a stability-focused patch release for the native HID backend.
- Automated validation covers build and the deferred OFFLINE gate behavior. Physical Logitech hardware validation may still be useful when confirming device-specific Windows HID behavior.
