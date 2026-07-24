# Local PowerTray 1.4.3 Installation

## Scope

- Build the current private `main` at `12a704680731813588c86f1757080d20d1fb80f8` as PowerTray 1.4.3.
- Back up the current `%LOCALAPPDATA%\PowerTray` installation.
- Replace only this Windows machine's formal installation with the newly built 1.4.3 light installer.
- Verify installed binary versions, edition, process health, and device endpoint.

## Exclusions

- No source-code changes beyond this task record and ignored design memory.
- No push, tag, GitHub Release, public asset, or remote-machine installation.
- No deletion of the backup or existing user configuration.

## Ordered Work

- [x] Confirm repository HEAD, worktree state, installed path, and installed 1.4.2 metadata.
- [x] Inspect the installer build path and build the current 1.4.3 light installer.
- [x] Verify installer and embedded binary version metadata.
- [x] Back up the existing installation and run the local replacement.
- [x] Verify the installed 1.4.3 runtime and archive this task.

## Acceptance Criteria

- Installed `PowerTray.dll` and `PowerTrayHID.dll` report `FileVersion 1.4.3.0` and the current private source commit in `ProductVersion`.
- `%LOCALAPPDATA%\PowerTray\installer-edition.txt` remains `light`.
- PowerTray and PowerTrayHID run successfully; `/health` and `/devices` respond.
- The pre-install 1.4.2 directory remains available in a timestamped backup.
- Repository product code, remotes, tags, releases, and other machines remain unchanged.

## Evidence

- Local repository is clean on `main` at `12a704680731813588c86f1757080d20d1fb80f8`, matching private `sync/main`.
- Existing installed `PowerTray.dll` reports `FileVersion 1.4.2.0` and `ProductVersion 1.4.2+13fe371d00ca8b72865a4a63d615a1c62aa1eae9`.
- No PowerTray or PowerTrayHID process was running at preflight.
- The standard release script requires the pinned update-signing private key, which remains only on VM102. For this local-only install, the same locked Release restore, framework-dependent publish, hidapi verification, and Inno light-installer configuration were run without copying the key or producing update-signature assets.
- `hidapi.dll` passed the Windows x64/export check with SHA-256 `FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1`.
- Built `bin/Release/local-install-20260725-0016/installer/PowerTraySetup-local-1.4.3.exe`: 3,787,673 bytes, SHA-256 `1C2F2FB54F6A128B7BF19465A8EA008183DB28213C7589A3A7CC0B39293E3ECD`.
- Published UI and HID assemblies both report `FileVersion 1.4.3.0` and `ProductVersion 1.4.3+12a704680731813588c86f1757080d20d1fb80f8`.
- Copied all 586 files from the 1.4.2 installation to `C:\Users\jiang\AppData\Local\Temp\PowerTray-1.4.2-before-1.4.3-20260725-0016`; the backed-up UI still reports `1.4.2+13fe371d00ca8b72865a4a63d615a1c62aa1eae9`.
- The local light installer completed silently with exit code `0`.
- Installed UI and HID assemblies both report `FileVersion 1.4.3.0` and `ProductVersion 1.4.3+12a704680731813588c86f1757080d20d1fb80f8`; `installer-edition.txt` remains `light`.
- PowerTray and PowerTrayHID started from `%LOCALAPPDATA%\PowerTray`. `/health` reports `running`, version `1.4.3+12a7046...`, restart count `0`, and two devices; `/devices` lists the PRO X2 SUPERSTRIKE mouse and PRO X 2 headset.

## Result

- This Windows machine's formal light installation is now the current private PowerTray 1.4.3 build at source commit `12a7046`. The verified 1.4.2 backup remains in the timestamped temp directory for rollback.
- No public/private remote, tag, release, update asset, or other machine installation was changed.
