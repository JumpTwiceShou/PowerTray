This release adds native headset support, richer diagnostics, and fixes device identity collisions.

## Highlights

- Added HID++ `0x1F20 ADC MEASUREMENT` battery support for G733/G535-style headsets.
- Added preliminary G522 support through Centurion `0x50` direct and bridge battery reads.
- Fixed device identity collisions caused by all-zero serial responses.
- Added diagnostics export with Logitech HID endpoint enumeration, unsupported HID context, raw identity responses, discovery sessions, and recent native backend events.
- Added a settings action to remove offline historical devices.
- Fixed Simplified Chinese UI mojibake.
- Fixed update downloads so full installs prefer `PowerTraySetup-full.exe` and lightweight installs prefer `PowerTraySetup.exe`.

## Downloads

- `PowerTraySetup.exe`: lightweight installer, requires .NET 8 runtime components.
- `PowerTraySetup-full.exe`: full installer with runtime included.

## Notes

- G522/G733 hardware was not physically verified in this build.
- Dolby/Atmos and Logitech G Hub settings are not managed by PowerTray.
