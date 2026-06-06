PowerTray 1.0.1 bug fix release.

Highlights:

- Adds per-device "follow global threshold" support; custom threshold sliders are disabled until the device opts out of the global threshold.
- Shows the current release version on the diagnostics page.
- Renames the per-device threshold label to "Low battery alert threshold".
- Fixes threshold slider thumb clipping while dragging.
- Disables device tray tooltips to avoid stuck battery tooltip popups.
- Improves offline detection when a receiver is still connected but the device no longer responds.
- Keeps the lightweight installer framework-dependent and the full installer self-contained without duplicated runtime payloads.

Validation:

- Debug build passed.
- Release installer build passed.
- Local lightweight install starts as PowerTray 1.0.1.
- Single-instance launch check passed.

Known note:

- Build currently reports the upstream MessagePack NU1902 advisory warning.
