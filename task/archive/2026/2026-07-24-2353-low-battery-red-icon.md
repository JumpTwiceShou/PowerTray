# Low-Battery Red Device Icon

## Scope

- Replace the current red-circle/exclamation alert frame with a red battery alert frame that retains each device's mouse, keyboard, or headset glyph.
- Reuse the existing 500 ms normal/alert blink cycle and runtime icon drawing path.

## Existing Work To Preserve

- Keep the uncommitted safe tooltip lifecycle changes in `LGSTrayUI/LogiDeviceIcon.xaml.cs`.
- Keep the archived tooltip task record unchanged.

## Exclusions

- No tooltip positioning or lifecycle changes beyond the already-present user work.
- No DPI-placement changes, new installer/version, runtime installation, commit, push, tag, or release.
- No AI-generated raster asset or fixed per-device PNG unless runtime composition cannot produce a clear icon.
- Do not change alert thresholds, notification policy, or blink timing.

## Ordered Work

- [x] Confirm the current alert path and existing icon layers.
- [x] Implement the red battery alert frame while preserving the device glyph.
- [x] Produce a local visual preview for mouse, keyboard, and headset.
- [x] Run focused Debug/Release builds and review the scoped diff.
- [x] Update local design memory and archive this task.

## Acceptance Criteria

- The alert frame contains no red circle or exclamation mark.
- Mouse, keyboard, and headset icons remain distinguishable.
- The battery portion is red in the alert frame.
- Existing blink timing alternates the normal device icon with the new red alert frame.
- No unrelated tracked files are changed.

## Evidence

- `BatteryIconDrawing.CreateAlertBitmap()` now tints only the battery fill/outline layers with alert red `#E81123`, then draws the current mouse, keyboard, or headset glyph normally.
- `DrawAlert()` no longer draws an ellipse or `!`; it uses the new composed alert bitmap.
- `TestLowBatteryAlertIcons()` verifies red battery pixels, retained non-red device pixels, and three distinct device renderings.
- Debug solution rebuild completed successfully. The new alert-icon test passed before the existing test runner reached its known installed-runtime named-pipe collision.
- Production-rendered previews were exported under `%TEMP%\PowerTray-low-battery-icon-preview-20260724-2356`.
- Full Debug and Release solution rebuilds completed successfully.
- `git diff --check` passed, and a source scan confirmed no `DrawEllipse` or exclamation-mark drawing remains.

## Result

- Replaced the old exclamation alert frame with a per-device red battery frame.
- Preserved the existing mouse, keyboard, and headset glyphs and the 500 ms blink cycle.
- Added focused render validation and visually reviewed production-generated previews.
- No install, version, commit, push, tag, or release action was performed.
