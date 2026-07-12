# PowerTray Update Signing

PowerTray releases use a detached ECDSA P-256 signature to authenticate the SHA-256 checksum file independently from GitHub Release hosting.

## Trust model

The application embeds only the public SubjectPublicKeyInfo value:

```text
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0G2H3dyoVsbph9xMRywEJb5BDhdQGQOrJcNwdwy6SDCautgU+Km+PIFk/sYDP3cA5IeJlcmcJoSOkb08Ja/xDw==
```

The private key is not stored in this repository or in release assets. The canonical release copy is currently stored on Windows VM102 at:

```text
%USERPROFILE%\.ssh\powertray_update_ecdsa.pem
```

The file ACL must remain restricted to the release operator account. The pinned public SPKI SHA-256 is:

```text
9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A
```

A PBS backup is tracked separately. Automatic-update signing must not be treated as operationally complete until a verified second copy exists.

## Required release assets

For each installer, publish all three files together:

```text
PowerTraySetup.exe
PowerTraySetup.exe.sha256
PowerTraySetup.exe.sha256.sig

PowerTraySetup-full.exe
PowerTraySetup-full.exe.sha256
PowerTraySetup-full.exe.sha256.sig
```

The `.sha256` file contains the installer SHA-256 and exact asset filename. The `.sig` file is a raw 64-byte ECDSA P-256 signature using SHA-256 and IEEE P1363 `r || s` encoding over the exact checksum-file bytes.

`build-installer.ps1` creates both checksum and signature files. It requires PowerShell 7 or later and defaults to the private-key path above.

## Verification order

The updater performs the following checks before offering execution:

1. Restrict release API, installer, checksum, signature, and final redirect URLs to trusted GitHub HTTPS hosts.
2. Verify the detached checksum signature with the embedded ECDSA public key.
3. Parse the signed checksum and require the exact installer filename.
4. Verify the downloaded installer SHA-256.
5. Recompute the SHA-256 immediately before execution while holding the file open with a restrictive share mode.

A GitHub account or repository compromise alone is therefore insufficient to publish a trusted replacement installer without the private signing key.

## Key rotation

The signing key cannot be silently replaced in GitHub Release assets. Rotation requires a normal PowerTray release that still verifies under the old key and embeds the new public key. Only after users have received that transition release may later releases be signed exclusively by the new key.

If the private key is lost without a transition release, existing installations cannot authenticate newly signed automatic updates. If the private key is suspected compromised, stop publishing automatic-update assets and ship a reviewed transition through a separately trusted distribution channel.

## Release checklist

- Confirm the private-key file ACL is restricted to the release operator.
- Run `LGSTrayHID/libhidapi/verify-hidapi.ps1`.
- Run locked restore, Release build, tests, and dependency audits.
- Run `build-installer.ps1` with the intended version.
- Confirm each `.sig` file is exactly 64 bytes.
- Verify the checksum signature and installer hash before upload.
- Upload each installer, checksum, and signature as one release set.
