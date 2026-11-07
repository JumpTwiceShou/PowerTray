## What's New

- Fixed excessive CPU usage after IPC disconnects and improved native HID shutdown and deferred offline handling.
- Improved IPC congestion and permission-error recovery, preventing stalled requests and shutdown; updated MessagePack to resolve a dependency security advisory.
- Fixed diagnostics export when the native backend is disabled, including G Hub-only configurations.
- Fixed missed low-battery alerts during communication congestion. Alert cycles now reset correctly after reconnection, battery recovery, or charging, while repeated readings within the same cycle remain deduplicated.
