param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = "1.4.1",
    [string]$Dotnet = "dotnet",
    [string]$Iscc = "",
    [string]$SigningKey = "$env:USERPROFILE\.ssh\powertray_update_ecdsa.pem"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishRoot = Join-Path $root "bin\$Configuration\publish\win-x64"
$frameworkPublish = Join-Path $publishRoot "framework-dependent"
$fullPublish = Join-Path $publishRoot "self-contained-full"
$installerOut = Join-Path $root "bin\$Configuration\installer"
$oldPayloadZip = Join-Path $root "PowerTrayInstaller\Payload\PowerTrayPayload.zip"
$innoScript = Join-Path $root "PowerTrayInstaller.iss"
$expectedSigningPublicKeySha256 = "9D08127794D5D85BF45DA60C8BC631CEBFE1E2D62A51140BFB6407FFC634570A"

function Find-Iscc {
    if ($Iscc -and (Test-Path -LiteralPath $Iscc)) {
        return (Resolve-Path -LiteralPath $Iscc).Path
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $knownPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $knownPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw "Inno Setup compiler ISCC.exe was not found. Install Inno Setup 6 or pass -Iscc <path>."
}

function Publish-AppPair {
    param(
        [string]$Output,
        [bool]$SelfContained
    )

    New-Item -ItemType Directory -Path $Output -Force | Out-Null

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    $commonArgs = @(
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", $selfContainedValue,
        "--no-restore",
        "-o", $Output,
        "/p:Version=$Version",
        "/p:PublishSingleFile=false",
        "/p:DebugType=None",
        "/p:DebugSymbols=false",
        "/p:PublishProtocol=FileSystem"
    )

    & $Dotnet publish (Join-Path $root "LGSTrayHID\LGSTrayHID.csproj") @commonArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for PowerTrayHID with exit code $LASTEXITCODE."
    }

    & $Dotnet publish (Join-Path $root "LGSTrayUI\LGSTrayUI.csproj") @commonArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for PowerTray with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $Output -Recurse -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
}

function Invoke-Inno {
    param(
        [string]$SourceDir,
        [string]$OutputBaseFilename,
        [int]$IncludeRuntime
    )

    & $script:IsccPath `
        "/DSourceDir=$SourceDir" `
        "/DOutputDir=$installerOut" `
        "/DOutputBaseFilename=$OutputBaseFilename" `
        "/DAppVersion=$Version" `
        "/DIncludeRuntime=$IncludeRuntime" `
        $innoScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compile failed for $OutputBaseFilename with exit code $LASTEXITCODE."
    }
}

function Write-InstallerChecksum {
    param(
        [string]$InstallerPath
    )

    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        throw "Installer output was not found: $InstallerPath"
    }

    $item = Get-Item -LiteralPath $InstallerPath
    $hash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileName = Split-Path -Leaf $InstallerPath
    $checksumPath = "$InstallerPath.sha256"
    $signaturePath = "$checksumPath.sig"
    Set-Content -LiteralPath $checksumPath -Encoding ASCII -NoNewline -Value "$hash  $fileName`n"

    if (-not (Test-Path -LiteralPath $SigningKey -PathType Leaf)) {
        throw "PowerTray update signing key was not found: $SigningKey"
    }

    $signer = [Security.Cryptography.ECDsa]::Create()
    try {
        $signer.ImportFromPem([IO.File]::ReadAllText($SigningKey))
        $publicKey = $signer.ExportSubjectPublicKeyInfo()
        $publicKeySha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($publicKey))
        if (-not $publicKeySha256.Equals($expectedSigningPublicKeySha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The update signing key does not match the public key pinned in PowerTray. Expected $expectedSigningPublicKeySha256, got $publicKeySha256."
        }

        $signature = $signer.SignData(
            [IO.File]::ReadAllBytes($checksumPath),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
        )
        if ($signature.Length -ne 64) {
            throw "Unexpected ECDSA P-256 signature length: $($signature.Length)."
        }
        [IO.File]::WriteAllBytes($signaturePath, $signature)
    }
    finally {
        $signer.Dispose()
    }

    [pscustomobject]@{
        Path = $InstallerPath
        ChecksumPath = $checksumPath
        SignaturePath = $signaturePath
        FileName = $fileName
        Size = $item.Length
        SHA256 = $hash
    }
}

Push-Location $root
try {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $installerOut -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $oldPayloadZip -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

    & (Join-Path $root "LGSTrayHID\libhidapi\verify-hidapi.ps1")

    & $Dotnet restore (Join-Path $root "PowerTray.sln") --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore --locked-mode failed with exit code $LASTEXITCODE."
    }

    Publish-AppPair -Output $frameworkPublish -SelfContained:$false
    Publish-AppPair -Output $fullPublish -SelfContained:$true

    $script:IsccPath = Find-Iscc
    Invoke-Inno -SourceDir $frameworkPublish -OutputBaseFilename "PowerTraySetup" -IncludeRuntime 0
    Invoke-Inno -SourceDir $fullPublish -OutputBaseFilename "PowerTraySetup-full" -IncludeRuntime 1

    $installerReports = @(
        Write-InstallerChecksum -InstallerPath (Join-Path $installerOut 'PowerTraySetup.exe')
        Write-InstallerChecksum -InstallerPath (Join-Path $installerOut 'PowerTraySetup-full.exe')
    )

    foreach ($report in $installerReports) {
        Write-Host ("Installer: {0} ({1:N0} bytes, SHA256 {2})" -f $report.Path, $report.Size, $report.SHA256)
        Write-Host ("Checksum: {0}" -f $report.ChecksumPath)
        Write-Host ("Signature: {0}" -f $report.SignaturePath)
    }
}
finally {
    Pop-Location
}
