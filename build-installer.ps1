param(
    [string]$Configuration = "Release",
    [string]$VersionSuffix = "",
    [string]$Dotnet = "F:\logi\.dotnet-sdk\dotnet.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "bin\Release\publish\win-x64\standalone"
$payloadDir = Join-Path $root "PowerTrayInstaller\Payload"
$payloadZip = Join-Path $payloadDir "PowerTrayPayload.zip"
$installerOut = Join-Path $root "bin\Release\installer"

Push-Location $root
try {
    Remove-Item -LiteralPath (Join-Path $root "bin\Release\publish\win-x64") -Recurse -Force -ErrorAction SilentlyContinue
    & $Dotnet publish LGSTrayHID\LGSTrayHID.csproj /p:PublishProfile=Standalone /p:Version=1.0.0$VersionSuffix
    & $Dotnet publish LGSTrayUI\LGSTrayUI.csproj /p:PublishProfile=Standalone /p:Version=1.0.0$VersionSuffix

    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
    Remove-Item -LiteralPath $payloadZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $payloadZip -Force

    & $Dotnet publish PowerTrayInstaller\PowerTrayInstaller.csproj -c $Configuration -r win-x64 --self-contained true -o $installerOut /p:PublishSingleFile=true
    Write-Host "Installer: $(Join-Path $installerOut 'PowerTraySetup.exe')"
}
finally {
    Pop-Location
}
