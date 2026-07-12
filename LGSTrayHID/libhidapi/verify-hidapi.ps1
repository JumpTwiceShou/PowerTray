[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$binary = Join-Path $PSScriptRoot 'hidapi.dll'
$expectedSha256 = '38BDA32F593C054CACAF95BEBCE36F9BACC7FBD0740F7B6F72F6D368FBC84B4D'

if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
    throw "Missing native dependency: $binary"
}

$actual = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expectedSha256) {
    throw "hidapi.dll SHA-256 mismatch. Expected $expectedSha256, received $actual."
}

$signature = Get-AuthenticodeSignature -LiteralPath $binary
if ($signature.Status -notin @('NotSigned', 'Valid')) {
    throw "hidapi.dll has an invalid Authenticode state: $($signature.Status)."
}

Write-Host "hidapi.dll verified: SHA-256 $actual; Authenticode $($signature.Status)."
