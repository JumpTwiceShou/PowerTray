[CmdletBinding()]
param(
    [string]$BinaryPath,
    [string]$ExpectedSha256 = 'FA2477A9D3BAB60C3CE92DE9D51319F945BFFB95B5D16ED5027739A51BF22FD1'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $BinaryPath = Join-Path $PSScriptRoot 'hidapi.dll'
}

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

$probeType = 'PowerTray.HidapiExportProbe' -as [type]
if ($null -eq $probeType) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerTray
{
    public static class HidapiExportProbe
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(IntPtr module);

        public static IntPtr Load(string fileName)
        {
            IntPtr module = LoadLibrary(fileName);
            if (module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to load native dependency.");
            }

            return module;
        }
    }
}
'@
    $probeType = [PowerTray.HidapiExportProbe]
}

$handle = $probeType::Load((Resolve-Path -LiteralPath $BinaryPath))
try {
    foreach ($export in $requiredExports) {
        if ($probeType::GetProcAddress($handle, $export) -eq [IntPtr]::Zero) {
            throw "hidapi.dll is missing required export: $export"
        }
    }
}
finally {
    [void]$probeType::FreeLibrary($handle)
}

$signature = Get-AuthenticodeSignature -LiteralPath $BinaryPath
if ($signature.Status -notin @('NotSigned', 'Valid')) {
    throw "hidapi.dll has an invalid Authenticode state: $($signature.Status)."
}

Write-Host "hidapi.dll verified: Windows x64; SHA-256 $actual; $($requiredExports.Count) required exports; Authenticode $($signature.Status)."
