## What's New

- Fixed device rediscovery and background service stability issues.
- Fixed battery polling issues after some devices reconnect.
- Improved local HTTP API safety.
- Improved settings saving and recovery.
- Improved installer cleanup behavior.
- Improved tray exit behavior.
- Improved settings window UI.
- Kept the global numeric battery toggle and added per-device display settings.
- Improved the diagnostics page with test notification, test blink, and stop blink actions.
- Fixed incorrect device type display for offline devices.
- Improved English and Chinese text, README, and troubleshooting notes.
- Added basic automated tests and build workflow.

## Notes

- The local HTTP API is still intended for local access only at `localhost:12321`.
- The installer still installs per user by default and does not require administrator privileges.
- User settings remain stored under `%APPDATA%\PowerTray`.

## Downloads

- `PowerTraySetup.exe`: lightweight installer, requires the .NET 8 runtime.
- `PowerTraySetup-full.exe`: full installer, includes the runtime.
