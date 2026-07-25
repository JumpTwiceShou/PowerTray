# USB Direct Recognition Latency

## Scope

- Reduce PowerTray-owned recognition latency after Windows exposes a direct Logitech USB HID++ endpoint.
- Prioritize newly created HID sessions ahead of refreshing existing sessions during rediscovery.
- Cache a protocol-confirmed direct device index (`0xFF` or `0x00`) by stable endpoint identity for fast same-process replug recognition.
- Use short, single-attempt probes only while guessing transport/device indexes.
- Yield failed device-readiness initialization to the existing bounded hotplug/unknown-device retry schedulers.
- Add stage timing diagnostics for future physical USB traces.
- Build and install a local-only 1.4.3 candidate for validation.

## Safety Boundaries

- Do not hard-code C094-only behavior or infer a device from its name.
- A direct index is cached only after a real device finishes HID++ initialization and the session did not identify receiver transport.
- C54D keeps its existing 600 ms timeout, two-attempt short-report recovery, and long-report fallback.
- Actual feature, identity, and battery commands keep their existing two-attempt behavior.
- Do not fabricate devices, battery values, online state, or successful physical timing.
- Do not change Centurion protocol behavior, tooltip/UI behavior, fullscreen behavior, releases, remotes, or other machines.
- Preserve the two unrelated untracked tooltip task files.

## Ordered Work

- [x] Inspect the current physical PnP state and recognition pipeline.
- [x] Identify fixed waits, speculative retries, session-order blocking, and the long initialization ping loop.
- [x] Prioritize newly created sessions during rediscovery.
- [x] Add protocol-confirmed direct-index caching.
- [x] Add fast speculative probes while preserving robust actual commands.
- [x] Shorten device-readiness initialization and rely on bounded outer retries.
- [x] Add focused regression tests and timing diagnostics.
- [x] Run complete Debug/Release builds and test programs serially.
- [ ] Build, back up, install, and validate the local candidate.

## Baseline Evidence

- This machine currently exposes the direct PRO X Wireless as Logitech PID `C094`, the PRO X 2 Centurion endpoint as `0AF7`, and the LIGHTSPEED mouse receiver as `C54D`.
- The previous physical C094 monitor recorded recovery windows of approximately 5.75 seconds and 14.62 seconds while the helper remained running.
- New sessions and reused sessions are currently processed in descriptor order, so an existing slow/incomplete session can delay a newly arrived USB session.
- A non-Centurion direct endpoint currently waits up to 1 second for receiver-register discovery, then scans `0xFF`, `0x00`, and `1-6` serially.
- Speculative HID++ pings currently inherit the general two-attempt 250 ms command policy.
- Device initialization currently permits up to ten ping calls while requiring three consecutive successes, even though bounded outer retries now exist.

## Implementation Evidence

- `RediscoveryPassPolicy` places newly created HID sessions before reused sessions while retaining stable order inside each group.
- `DirectDeviceProbeCache` accepts only protocol-confirmed direct indexes `0xFF`/`0x00` and only when a stable serial/container endpoint identity exists.
- Non-C54D speculative probes use one 150 ms attempt; normal HID++ feature/identity/battery calls still use two attempts.
- `HidCommandAttemptPolicy` forces C54D short-report recovery to remain at two attempts even when a caller requests one speculative attempt.
- Receiver-register discovery now uses a bounded 300 ms wait, and the no-receiver settle delay is 50 ms.
- Initialization readiness now requires two quick consecutive replies and yields failures to the existing bounded retry schedulers.
- Discovery diagnostics record stage names and elapsed milliseconds only while a session has no known device.
- Debug and Release solution builds passed on 2026-07-25.
- Debug and Release `PowerTray.Tests` both passed, including new session-order, attempt-policy, and direct-cache regression coverage.
