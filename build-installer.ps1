param(
    [string]$Configuration = "Release",
    [string]$Version = "1.2.1",
    [string]$Dotnet = "F:\logi\.dotnet-sdk\dotnet.exe",
    [string]$Iscc = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishRoot = Join-Path $root "bin\Release\publish\win-x64"
$frameworkPublish = Join-Path $publishRoot "framework-dependent"
$fullPublish = Join-Path $publishRoot "self-contained-full"
$installerOut = Join-Path $root "bin\Release\installer"
$oldPayloadZip = Join-Path $root "PowerTrayInstaller\Payload\PowerTrayPayload.zip"
$innoScript = Join-Path $root "PowerTrayInstaller.iss"

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

Push-Location $root
try {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $installerOut -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $oldPayloadZip -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

    Publish-AppPair -Output $frameworkPublish -SelfContained:$false
    Publish-AppPair -Output $fullPublish -SelfContained:$true

    $script:IsccPath = Find-Iscc
    Invoke-Inno -SourceDir $frameworkPublish -OutputBaseFilename "PowerTraySetup" -IncludeRuntime 0
    Invoke-Inno -SourceDir $fullPublish -OutputBaseFilename "PowerTraySetup-full" -IncludeRuntime 1

    Write-Host "Installer: $(Join-Path $installerOut 'PowerTraySetup.exe')"
    Write-Host "Full installer: $(Join-Path $installerOut 'PowerTraySetup-full.exe')"
}
finally {
    Pop-Location
}
