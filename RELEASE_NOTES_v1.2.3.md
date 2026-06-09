# PowerTray 1.2.3

This release fixes tray tooltip cleanup for per-device battery icons.

## Highlights

- Fixed per-device tray tooltip text sometimes remaining on screen after hovering a battery icon.
- Restored controlled rich tray tooltip handling for device icons instead of relying on the Windows shell tooltip path.
- Added explicit tooltip close handling when tray icons redraw, battery text changes, or device icons are disposed.

## Downloads

- `PowerTraySetup.exe`: lightweight installer, requires .NET 8 runtime components.
- `PowerTraySetup-full.exe`: full installer with runtime included.
