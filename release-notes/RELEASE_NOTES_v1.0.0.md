PowerTray 1.0.0 initial public release.

Highlights:

- Native-only Logitech HID++ battery backend using hidapi.
- No dependency on Logitech G Hub backend or localhost:9010.
- Bilingual English/Simplified Chinese application and installer.
- Modern settings window with per-device low battery threshold, alias, Windows notification, tray blinking, and pause controls.
- Fullscreen notification suppression and quiet hours.
- Single-file Windows x64 installer with optional Start with Windows.
- Compatible HTTP battery API at /devices and /device/{id}.

Validated devices:

- PRO X2 SUPERSTRIKE Wireless Mouse
- PRO X 2 Lightspeed Gaming Headset

Known note:

- Native mode does not provide G Hub mileage data; mileage is reported as -1.
- Build currently reports the upstream MessagePack NU1902 advisory warning.
