# PowerTray CI and Issue #4 investigation

- Task ID: `2026-07-12-0157-powertray-ci-issue-investigation`
- Created: `2026-07-12 01:57 +09:00`
- Status: `completed`
- Scope: read-only investigation of GitHub Actions failures in `PowerTray` and `PowerTray-dev`, plus public Issue #4 and its diagnostics package.

## Goals

- Identify why both repositories contain failed Build runs.
- Explain why Build runs are created.
- Inspect public Issue #4 and determine the likely software cause.
- Make no source-code, release, issue, or public-repository changes.

## CI findings

- `.github/workflows/build.yml` is present in both repositories.
- It runs on every push to `main` and every pull request.
- The job performs `dotnet build`, the custom `PowerTray.Tests` executable, and a NuGet vulnerability audit.
- There are no path filters, so documentation, AGENTS, and task-only commits also trigger the full Build workflow.

### Public repository

- The latest `main` Build is successful: run `28186106890`, commit `1aa11403fb10` (`v1.4.1`).
- The visible failed runs are historical pull-request runs from PR #3.
- The most recent failed public run, `28185662716`, failed to compile `DeferredOfflineGate.cs` because the fallback lambda syntax around `static` was invalid.
- Commit `25dc2c9975ea` fixed the lambda syntax. The following PR run and merged `main` run succeeded.
- Result: no current public-main CI failure remains.

### Private repository

- The latest private Build is successful: run `29156910829`, commit `f40068846ba9`.
- Failed run `29156611842` was triggered by the task/AGENTS-only commit `d8605759d7e4`; the application source and tests were not changed by that commit.
- Build compilation succeeded, but `TestDeferredOfflineGateDelaysOffline` failed because `TryDefer` returned false after a wall-clock grace window of only 40 ms.
- The next commit only moved the task file to the archive; it made no test or gate code change, and the next CI run succeeded.
- Result: this was a timing-dependent flaky test, not a persistent application build failure.

## Issue #4 evidence

- Affected release: `1.4.1`.
- Device: `G502 X LIGHTSPEED`.
- Reported behavior: device is initially online, then becomes offline after waiting; restarting repeats the sequence.
- The diagnostics package shows an initial valid battery read of 85%, then an offline cached device state.
- The last valid update was `2026-07-02T06:52:02+03:00`; diagnostics were generated at `07:39:18+03:00`.
- Native diagnostics fields are empty because the UI timed out waiting for a native diagnostics snapshot.

## Likely root cause

The evidence strongly points to the periodic battery polling/offline state machine rather than unsupported hardware:

1. Initial discovery and battery reading succeed, proving that the device, receiver, identity, and battery feature are recognized.
2. Release polling runs every 600 seconds.
3. `HidppDevice.UpdateBattery()` immediately sends OFFLINE after one failed 150 ms HID++ ping or one failed battery read.
4. The 3-second `DeferredOfflineGate` only protects explicit HID hotplug-left events; it does not protect periodic polling failures.
5. On a later successful poll, `SignalBatteryUpdate()` clears `_offlineSignalled`, but suppresses the UPDATE IPC message when the battery result equals the previous result.
6. Therefore a single transient polling failure can mark the UI offline, and later successful reads with the same percentage can fail to bring the UI online again. Restarting recreates the device state and makes it appear online, matching the report.

The exact first transport failure cannot be distinguished from the submitted package because the diagnostics request path is incomplete:

- `NativeDiagnosticsClient.RequestAsync()` subscribes and waits for a future snapshot, but does not send `NATIVE_DIAGNOSTICS_REQUEST`.
- The HID helper does not handle that request type.
- If no device event occurs during the 2-second export wait, the UI reports that PowerTrayHID did not respond even when the process may still be alive.

## Recommended corrections

- Require multiple consecutive ping/battery failures before sending OFFLINE, with a short retry or rediscovery first.
- On recovery from `_offlineSignalled`, always publish an UPDATE or INIT even when the battery value is unchanged.
- Replace the 40 ms wall-clock CI test with deterministic time control or a synchronization-driven test.
- Implement a real native diagnostics request/response flow with a request ID so exported diagnostics include endpoint, failure, ping, and recent-event evidence.
- Optionally add workflow `paths-ignore` rules for task/AGENTS/documentation-only changes while retaining CI for source and build-system changes.

## Validation and final state

- Authenticated GitHub Actions history, PR #3, Issue #4, and the attached diagnostics package were inspected.
- Relevant `v1.4.1` source was compared with current private `main`; the HID polling, recovery, diagnostics-client, tests, and workflow files are unchanged.
- No source code, GitHub issue, workflow setting, release, commit, push, or public repository was modified.
- Issue #4 remains open and the likely state-recovery defect remains present in current private `main`.
