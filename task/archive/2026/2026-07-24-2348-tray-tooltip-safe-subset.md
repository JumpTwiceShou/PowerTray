# Tray Tooltip Safe Subset

## Scope

- Apply only the reviewed low-risk parts of the `sync/fable5` tooltip changes to the current `adbf768` source baseline.
- Configure tooltip theme before opening, avoid closing on ordinary icon redraws or no-op tooltip notifications, and make disposal re-entrancy safe.

## Exclusions

- No absolute tooltip placement, cursor/DPI coordinate calculation, native window movement, opacity gating, popup rebuilding, or cursor-radius polling.
- No version bump, installer build/install, runtime replacement, commit, push, tag, release, or public remote change.
- No unrelated DPI icon, G Hub, updater, or syntax changes.

## Ordered Work

- [x] Confirm `main` is clean at `adbf768`; `sync/main` is two documentation-only commits ahead.
- [x] Implement the minimal tooltip lifecycle changes.
- [x] Run focused build/test validation and review the scoped diff.
- [x] Update local design memory with the final implementation decision.

## Acceptance Criteria

- Tooltip styling is configured during preview-open without changing WPF placement.
- Battery icon redraws do not force-close an open tooltip.
- Repeated `DisplayToolTipString` notifications with unchanged text do not close the tooltip.
- Disposal remains idempotent and unregisters the preview-open handler.
- No forbidden positioning or polling code is introduced.

## Evidence

- `git diff --check` passed after the source edit.
- The scoped diff changes only `LGSTrayUI/LogiDeviceIcon.xaml.cs` plus this task record.
- A targeted pattern scan found no absolute placement, cursor/DPI conversion, native movement, opacity gating, or cursor-radius polling in the changed source file.
- SDK 8.0.423 MSBuild locked restore and full Debug solution rebuild completed with 0 warnings and 0 errors without changing `global.json`.
- A focused Release rebuild of `LGSTrayUI` and its dependencies completed successfully.
- The test program completed all calls through `TestDiagnosticsPrivacyScope`; its existing directional IPC test then stopped at `Program.cs:575` because the installed formal PowerTray instance owns all configured named-pipe instances. The installed app was not stopped because runtime mutation is excluded.

## Result

- Applied the reviewed safe subset to `LGSTrayUI/LogiDeviceIcon.xaml.cs`.
- Debug and Release compilation passed; static scope checks passed.
- No runtime, version, installer, commit, push, or release action was performed.
- Rapid multi-device hover behavior still requires maintainer visual validation in a separately installed candidate if this patch is later packaged.
