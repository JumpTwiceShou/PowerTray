# Native hidapi dependency

PowerTray currently ships a custom Windows x64 build identified as **hidapi 0.14.0 with hotplug callback support**.

## Verified binary

- File: `hidapi.dll`
- SHA-256: `38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D`
- Authenticode: unsigned
- First repository commit containing this binary: `39bd785f9c52664c091c2877b856639734d319e6`
- Required custom exports:
  - `hid_hotplug_register_callback`
  - `hid_hotplug_deregister_callback`

Run `./verify-hidapi.ps1` before building or publishing. CI runs the same verification and fails if the binary changes.

## Recovered provenance

The committed DLL is byte-for-byte identical to the binary in [`andyvorld/LGSTrayBattery`](https://github.com/andyvorld/LGSTrayBattery):

- Upstream path: `LGSTrayHID/libhidapi/hidapi.dll`
- Upstream introduction commit: [`ed15f98f253af4ad30fe3f15fef40d7c983d4d00`](https://github.com/andyvorld/LGSTrayBattery/commit/ed15f98f253af4ad30fe3f15fef40d7c983d4d00)
- Upstream change: PR #82, `v3 rewrite`, merged 2023-12-09
- Upstream directory note: `Custom build of hidapi 0.14.0 with hotplugging support`

The source lineage for the custom API has also been recovered from the official [`libusb/hidapi`](https://github.com/libusb/hidapi) repository:

- Version baseline: tag [`hidapi-0.14.0`](https://github.com/libusb/hidapi/releases/tag/hidapi-0.14.0), commit `d3013f0af3f4029d82872c1a9487ea461a56dee4`
- Hotplug development branch: `connection-callback`
- Official PR: [`libusb/hidapi#299`](https://github.com/libusb/hidapi/pull/299), merged into `connection-callback`, not the release/master branch
- Core hotplug implementation commit: [`1b0b6acce5505aaa66b550f648c7662a03a53f7e`](https://github.com/libusb/hidapi/commit/1b0b6acce5505aaa66b550f648c7662a03a53f7e), `hotplug: Add ability to register device connection/disconnection callback (#299)`
- Latest `connection-callback` commit preceding the DLL PE timestamp: `eea8cac0793754b9a160c064646c9a6da7545a55` (2023-11-12), making it the strongest source-snapshot candidate, but not a proven exact build commit
- Later surviving mirror/fork: [`OpenRGBDevelopers/hidapi-hotplug`](https://gitlab.com/OpenRGBDevelopers/hidapi-hotplug)

The hotplug commit adds the exact API names PowerTray imports, including the Windows `CM_Register_Notification` implementation. Because the hotplug work lived on `connection-callback` while the 0.14.0 tag came from the release/master lineage, the upstream note correctly describes this as a custom 0.14.0 hotplug build. The current OpenRGB prebuilt `hidapi-hotplug.dll` is newer and has a different SHA-256; it is not the binary shipped by PowerTray.

## Remaining reproducibility limitation

Additional evidence recovered directly from the PE image:

- File/Product version: `0.14.0`
- Company: `libusb/hidapi Team`
- PE timestamp: `2023-11-15T12:56:25Z`
- Embedded PDB path: `E:\repos\hidapi\windows\x64\Release\hidapi.pdb`
- PE linker version: `14.38`
- Architecture/subsystem: PE32+ x64, Windows GUI subsystem

This establishes that the artifact was built from a Windows x64 Release configuration in a checkout named `hidapi`. The exact Visual Studio installation, complete compiler flags, project-file state, and clean build script used for that build have not been recovered. The binary origin and source/patch lineage are now documented, but a byte-for-byte clean rebuild is still not proven.

A future replacement of `hidapi.dll` must include all of the following in the same change:

1. Exact upstream repository and commit.
2. All local patches as source files.
3. Pinned compiler, CMake generator, architecture, and build flags.
4. A build script that produces the DLL from a clean checkout.
5. Updated SHA-256 in `verify-hidapi.ps1`.
6. Export verification for the two custom hotplug APIs.
7. Review evidence comparing the rebuilt DLL with the committed artifact.

Until the original build recipe is recovered or the binary is rebuilt from a documented source checkout, the pinned hash proves integrity and the links above establish lineage, but neither proves byte-for-byte reproducibility of the existing artifact.
