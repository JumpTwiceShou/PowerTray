# PowerTray 1.5.0 Custom Tooltip Live Theme Refresh

## Scope

- Fix the PowerTray custom tray tooltip so an already-created device tooltip immediately follows manual/system app-theme changes.
- Reapply the current application tooltip brushes immediately before a custom tooltip opens as an idempotent fallback for delayed or stale popup content.
- Verify the expected Infisical-to-host signing-key synchronization path after the maintainer identified the missing local key as unexpected; if the managed configuration contains it, restore the key without exposing its contents and regenerate signed checksum artifacts.
- Keep version `1.5.0`.

## Exclusions

- Do not recreate, close, reposition, delay, force-open, or otherwise change tooltip popup lifecycle.
- Do not change Windows native hover, disabled hover, coordinates, DPI behavior, timers, fullscreen behavior, HID/device discovery, or zero-device behavior.
- Do not install, push, publish, or synchronize the candidate to other development devices without a separate user request. Managed secret synchronization to this host is now in scope.
- Preserve unrelated untracked task files.

## Baseline And Rollback

- Repository baseline: `839b662`; current product baseline: `e9ca0b7`.
- Rollback is the source diff and resulting product commit from this task.
- The currently installed `1.5.0+e9ca0b7...` build is not changed by this task.

## Ordered Work

- [x] Read shared/project rules and local design memory; verify branch, HEAD, upstream, and working tree.
- [x] Bind the custom tooltip surface to explicit live application brushes.
- [x] Refresh existing custom tooltip surfaces on app-theme changes.
- [x] Refresh once more immediately before custom tooltip open.
- [x] Add focused WPF/STA production-path coverage for proactive and open-time refresh.
- [x] Run the minimum Debug/Release builds and test programs.
- [x] Review and commit the scoped product diff.
- [x] Build local light/full installers from the exact product commit and record hashes.
- [x] Verify the Infisical signing-key synchronization path without printing secret material.
- [x] Seed the missing remote secret from the trusted VM102 source after explicit maintainer authorization.
- [x] Regenerate and verify signed checksum artifacts after restoring the pinned key.
- [x] Update local design memory, archive this task, and record the final result.

## Acceptance Criteria

- Existing PowerTray custom tooltip content changes light-to-dark and dark-to-light without recreating the device icon.
- A deliberately stale custom tooltip surface is corrected by the production preview-open path.
- Custom tooltip background, border, and text foreground all match the active application palette.
- Disabled and Windows native modes retain their existing registration behavior.
- No popup placement or lifecycle behavior changes.

## Evidence

- Live report: after selecting dark mode, one existing custom tooltip remained light while another tooltip appeared dark.
- `LogiDeviceIcon.xaml` creates one custom `Border`/`TextBlock` resource per icon with dynamic application brushes.
- `CheckThemePropertyChanged` currently redraws only the battery icon for taskbar-theme changes.
- `OnPreviewTrayToolTipOpen` currently clears only the Hardcodet wrapper chrome.
- SDK 8.0.423 Debug and Release solution builds completed with zero warnings/errors.
- Debug and Release `PowerTray.Tests.exe` both passed, including real `ThemeService` setting changes and the production `PreviewTrayToolTipOpen` routed event.
- Running the test DLL directly was rejected because its isolated child saw `dotnet.exe` as `Environment.ProcessPath` and the unavailable pinned 8.0.422 SDK; rerunning the generated test apphost is the valid test invocation and passed.
- The installed `1.5.0+e9ca0b7...` process was stopped through supported `--shutdown` for IPC isolation and restarted unchanged. The UI and HID helper processes are running again.
- Product commit `133578c3d0b54d1eb27da6c5b75c80f46891c354` contains only `LogiDeviceIcon.xaml.cs` and focused production-path test changes.
- Framework-dependent UI/HID assemblies report `FileVersion 1.5.0.0` and `ProductVersion 1.5.0+133578c3d0b54d1eb27da6c5b75c80f46891c354`.
- Local light installer: `bin/Release/powertray-1.5.0-133578c/installer/PowerTraySetup-local-1.5.0.exe`, 3,820,603 bytes, SHA-256 `BE76545B6566B2E50C78F577C52C9EA55ABCA26B780642EC6DCD08B2572A6E3D`.
- Local full installer: `bin/Release/powertray-1.5.0-133578c/installer/PowerTraySetup-full-local-1.5.0.exe`, 51,599,025 bytes, SHA-256 `7C8429FD5A2A3FE1CB3B8D8B164046D2A4CE12FC08B36DFF1E466D5E83049718`.
- Matching `.sha256` files exist. The installer executables remain Authenticode `NotSigned`; update authenticity is provided by the detached checksum signatures described below.
- Maintainer correction: Infisical is expected to synchronize the release-signing material to managed development machines. The earlier local-file-only conclusion is no longer final; the configured synchronization path is being inspected.
- Live Infisical query succeeded against `dev:/projects/logi`, but the path contains zero keys and `POWERTRAY_UPDATE_ECDSA_PEM_B64` is absent. Authentication, endpoint, project ID, and path access are functional.
- The managed current-Windows `.env.local` exists but has no project keys, matching the empty remote path.
- Historical PowerTray and HomeLab tasks show the planned initial private-key upload was blocked before Infisical was called and remained incomplete; `sync-all-projects` is intentionally remote-to-local only.
- Read-only VM102 verification through the managed `windowsvm` SSH alias confirms the canonical source file still exists at the documented path and is 227 bytes. Its contents were not printed or transferred.
- After explicit authorization, VM102 validated the source key against pinned SPKI SHA-256 `9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A`, ran the managed `push-project-env.ps1` dry-run, applied the one named key to `dev:/projects/logi`, verified the remote key name, and deleted its ACL-restricted temporary dotenv file.
- Current Windows exported `/shared/common` plus `/projects/logi` atomically into the Git-ignored `.env.local`; only `POWERTRAY_UPDATE_ECDSA_PEM_B64` is present.
- Current Windows decoded the synchronized value in memory, independently verified the same pinned public-key fingerprint, and restored `%USERPROFILE%\.ssh\powertray_update_ecdsa.pem` at 227 bytes. Its ACL allows only `DESKTOP-3090\jiang` and `NT AUTHORITY\SYSTEM`.
- Both checksum files now have 64-byte ECDSA P-256 SHA-256 IEEE-P1363 `.sig` files. Each signature was independently verified against the exact public SPKI embedded by `UpdateService`; installer hashes remain unchanged.

## Decisions

- Use direct assignment of the current application brush resources to the existing custom tooltip elements. This replaces potentially cached/frozen resource values without moving or recreating the popup.
- Use both event-driven refresh and open-time fallback: the former updates already-created content immediately, while the latter covers lazy/stale popup resolution.

## Blockers

- None.

## Commits

- Product: `133578c3d0b54d1eb27da6c5b75c80f46891c354` (`fix: refresh custom tray tooltip theme live`).
- Task archival is committed separately after recording this final state.

## Device Sync

- Not authorized and not performed.

## Final Result

- Existing custom tooltip surfaces now refresh their background, border, and foreground immediately when the effective app theme changes.
- Preview-open reapplies the same current brushes as an idempotent fallback; it does not recreate, close, move, or delay the popup.
- Disabled and Windows native hover registrations are unchanged.
- Candidate installers were built locally but not installed, pushed, published, or synchronized. The running installation remains `1.5.0+e9ca0b7...`.
- Infisical synchronization and local release-key restoration are complete on the current Windows control PC. No private-key or Base64 value was printed, logged in the task, or committed.
- Both local candidate checksum files now have valid detached update signatures. The installer executables themselves remain Authenticode `NotSigned`, which is a separate certificate-based mechanism.
- Root cause confirmed: the Infisical synchronization mechanism works, but the release-signing secret was never seeded remotely, so there is nothing for managed hosts to download.
