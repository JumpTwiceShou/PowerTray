# Modernize tray device scrollbar

## Scope

- Create and work on a dedicated feature branch.
- Restyle the tray menu device-list scrollbar to match PowerTray's modern UI.
- Hide the scrollbar when the tray device list contains four or fewer devices.

## Exclusions

- No changes to device discovery, battery reporting, or settings behavior.
- No public release, push, or merge into `main` without explicit user approval.
- Preserve unrelated local files and work, including the existing untracked `NUL` entry.

## Ordered Work

- [x] Verify Git state and create the feature branch.
- [x] Locate the tray device-list scroll container and existing visual design tokens.
- [x] Implement the scoped scrollbar styling and four-device threshold behavior.
- [x] Run focused build/tests and inspect the rendered tray menu.
- [x] Review the scoped diff and staged state.
- [x] Update the local design memory, record the final result, and archive this task.

## Acceptance Criteria

- The tray device-list scrollbar visually matches the rest of the application.
- With zero through four devices, no scrollbar is shown and the list does not need to scroll.
- With five or more devices, vertical scrolling remains available and the modern scrollbar is shown only when needed.
- The affected project builds successfully and focused behavioral validation passes.
- All implementation work remains on the dedicated feature branch for user review.

## Evidence

- Git branch verification: `feat/modernize-tray-device-scrollbar` at `adbf76819c64ca780a5d26258336ab522d617eaa`, with no upstream configured.
- The pre-existing untracked `NUL` entry remains untouched and excluded.
- Root cause: `TrayMenuItemStyle` used the default WPF submenu `ScrollViewer`, so its system scrollbar did not match the custom settings UI and had no device-count threshold.
- `NotifyIconResources.xaml` now caps the device submenu at four 28-DIP rows, disables vertical scrolling for counts 0–4, enables `Auto` overflow at 5+, and applies a compact arrowless 9-DIP scrollbar matching the settings-page geometry and colors.
- `PowerTray.Tests` now validates the four/five-device boundary, the 112-DIP viewport cap, and the custom scrollbar template.
- Locked restore completed after stale local `obj` assets omitted an already-locked dependency; subsequent Debug build passed with 0 warnings and 0 errors.
- `dotnet run --project PowerTray.Tests\\PowerTray.Tests.csproj -c Debug --no-build --no-restore` passed.
- Final focused Debug build passed with 0 warnings and 0 errors; the final `PowerTray.Tests` run passed after expanding coverage across every count from 0 through 4 plus the 5-device overflow boundary.
- A temporary WPF host loaded the product `NotifyIconResources.xaml` and measured the real submenu template: 4 devices requested `Disabled`, computed `Collapsed`, viewport/extent `4/4`, and scrollable height `0`; 5 devices requested `Auto`, computed `Visible`, viewport/extent `4/5`, scrollbar width `9`, and scrolling to the end moved the logical offset from `0` to `1`.
- Windows Computer Use could not capture the preview because window activation failed with `GetCursorPos failed: Access denied`; the fallback bounded screenshot path was stopped after it hung. No preview process or temporary preview directory remains.

## Decisions

- Feature branch: `feat/modernize-tray-device-scrollbar`.
- The fetched private `origin/main` was one documentation-only commit ahead of local `main`; after confirming it only archived the completed v1.4.2 task, the feature branch was based directly on that latest remote commit without changing local `main`.
- Keep the change local to the tray resource dictionary rather than refactoring settings resources; the tray scrollbar mirrors the existing settings scrollbar dimensions, corner radius, and neutral colors while retaining popup-specific theme handling.

## Blockers

- None.

## Commits

- `9d7e011280db78e45054e35d94d06276007375fc` - `Modernize tray device scrollbar`

## Device Sync

- Not requested; no push or cross-device sync is authorized for this task.

## Final Result

- Completed on `feat/modernize-tray-device-scrollbar` without changing local `main`, pushing a remote branch, or publishing a release.
- The tray device list now shows no scrollbar for 0–4 devices and uses the compact PowerTray scrollbar with working overflow navigation for 5+ devices.
- Focused build, automated regression tests, native WPF layout measurement, scroll interaction evidence, diff review, and temporary-process cleanup all passed.
