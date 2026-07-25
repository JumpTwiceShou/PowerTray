# PowerTray 1.5.0 Audit Remediation

## Scope

- Implement the accepted GPT Pro audit fixes against `a5975e3`.
- Add restart-applied tray tooltip modes: disabled, Windows native, and PowerTray custom.
- Preserve the existing custom tooltip disposal fix and avoid coordinate/DPI/window-position workarounds.
- Include reviewed English, Simplified Chinese, and Japanese localization changes.
- Align source, installer, build script, release notes, and lock metadata to version `1.5.0`.

## Exclusions

- No real Logitech hardware validation.
- No real Shell hover, physical multi-monitor, mixed-DPI, or fullscreen acceptance matrix.
- No public remote push, tag, GitHub Release, or asset publication.
- No Authenticode signing without the trusted signing key/certificate.
- No speculative C54D cache eviction without hardware evidence.
- Do not change the intentional `deviceCount=0` behavior.

## Ordered Work

- [x] Record the exact baseline, preserve unrelated untracked task files, and validate the audit patches.
- [x] Apply and review the reliability/security changes instead of importing GPT-generated commits.
- [x] Correct settings persistence so mutation, serialization, and atomic replacement share an ordered transaction boundary.
- [x] Add hotplug-registration diagnostics and remove Release device metadata output.
- [x] Implement mutually exclusive tooltip modes, safe restart, migration, and three-language settings UI.
- [x] Set version `1.5.0` and add release notes.
- [ ] Run focused Debug/Release builds, tests, dependency audit, native verification, installer build, and diff checks.
- [ ] Commit the reviewed source and archive this task with exact evidence and remaining proof gaps.

## Acceptance Criteria

- Extreme IPC timestamps cannot overflow the validation path.
- Concurrent settings operations cannot share a fixed temporary file or write an older state after a newer transaction.
- One unexpected battery-poll exception does not permanently end the device poll loop.
- Crash logging is best effort and cannot replace the original fatal exception.
- Release builds do not capture MessagePipe subscription stacks or write raw device tooltip data to stdout.
- Hotplug registration failure is visible in diagnostics and the existing rediscovery fallback remains active.
- Remote HTTP configuration cannot create a plaintext non-loopback listener.
- The three tooltip modes are persisted, mutually exclusive, and applied only after restart.
- Existing/invalid settings migrate to PowerTray custom tooltip mode.
- The setting explains why restart is required and offers restart now/later in English, Simplified Chinese, and Japanese.
- Automated validation passes; skipped real-hardware and real-Shell checks remain explicitly unverified.

## Rollback

- Revert the scoped 1.5.0 commits.
- The pre-change source remains available at `a5975e3ac345e01cdc785141df297c9d2eeaf035`.
- Existing installed PowerTray 1.4.3 is not replaced by this task.

## Evidence

- Baseline: `a5975e3ac345e01cdc785141df297c9d2eeaf035`.
- GPT Pro evidence and patches are preserved under `C:\Users\jiang\Downloads`.
- Proposed fixes patch SHA-256: `19AA209FE5A4F337BE4CF4B0CA68B48232C81E6DDB4FC66C2CD552F6458EAB27`.
- Tooltip modes patch SHA-256: `207365CCB513B2D8FC276181BC566A1C85DE8684701E08790047394F9F78D727`.
- Both patches applied in dependency order as working-tree changes; GPT-generated commits were not imported.
- Rejected the patch's input-arming ComboBox handlers and self-dispatching icon disposal. Selection is now value-driven for mouse, keyboard, touch, and accessibility input, while icon disposal retains the collection's existing UI-thread ownership.
- Settings writes use unique temporary files, durable flush/replace, and a revision coordinator that re-snapshots when a newer save is requested before an older write completes.
- HID hotplug callback registration and deregistration results are checked. Failed registration records a redacted diagnostic while startup and periodic rediscovery continue.
- Legacy remote HTTP configuration is retained only for deserialization compatibility; all addresses outside `localhost`, `127.0.0.1`, and `::1` fail closed to `localhost`.
- Release device updates no longer write names and battery values to standard output; MessagePipe stack capture is Debug-only.
- UI, HID helper, installer default, build-script default, release notes, and project-reference lock metadata are aligned to `1.5.0`.
- SDK 8.0.423 MSBuild Debug and Release solution builds completed with zero warnings and errors without changing the repository-pinned `global.json`.
- Complete Debug and Release test programs passed after supported shutdown of the installed build; the original installed 1.4.3 UI/helper were restarted after each isolated IPC run.
- NuGet reported no known vulnerable direct or transitive package in `PowerTray.Tests` and its project graph.
- Formal hidapi verification passed for Windows x64, the pinned SHA-256, and all 12 required exports; Authenticode remains `NotSigned`.
- Real Logitech hardware, real Shell hover, mixed-DPI/multi-monitor, and fullscreen acceptance were explicitly waived and were not reclassified as passed.
