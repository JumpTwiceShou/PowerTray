[CmdletBinding()]
param(
    [string]$BinaryPath = (Join-Path $PSScriptRoot 'hidapi.dll'),
    [string]$ExpectedSha256 = '38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D'
)

$ErrorActionPreference = 'Stop'
$requiredExports = @(
    'hid_init',
    'hid_exit',
    'hid_enumerate',
    'hid_free_enumeration',
    'hid_open_path',
    'hid_close',
    'hid_write',
    'hid_read_timeout',
    'hid_version',
    'hid_winapi_get_container_id',
    'hid_hotplug_register_callback',
    'hid_hotplug_deregister_callback'
)

if (-not (Test-Path -LiteralPath $BinaryPath -PathType Leaf)) {
    throw "Missing native dependency: $BinaryPath"
}

$actual = (Get-FileHash -LiteralPath $BinaryPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $ExpectedSha256.ToUpperInvariant()) {
    throw "hidapi.dll SHA-256 mismatch. Expected $ExpectedSha256, received $actual."
}

$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $BinaryPath))
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x8664) {
    throw ('hidapi.dll is not Windows x64 PE32+. Machine: 0x{0:X4}.' -f $machine)
}

$handle = [Runtime.InteropServices.NativeLibrary]::Load((Resolve-Path -LiteralPath $BinaryPath))
try {
    foreach ($export in $requiredExports) {
        $address = [IntPtr]::Zero
        if (-not [Runtime.InteropServices.NativeLibrary]::TryGetExport($handle, $export, [ref]$address)) {
            throw "hidapi.dll is missing required export: $export"
        }
    }
}
finally {
    [Runtime.InteropServices.NativeLibrary]::Free($handle)
}

$signature = Get-AuthenticodeSignature -LiteralPath $BinaryPath
if ($signature.Status -notin @('NotSigned', 'Valid')) {
    throw "hidapi.dll has an invalid Authenticode state: $($signature.Status)."
}

Write-Host "hidapi.dll verified: Windows x64; SHA-256 $actual; $($requiredExports.Count) required exports; Authenticode $($signature.Status)."
