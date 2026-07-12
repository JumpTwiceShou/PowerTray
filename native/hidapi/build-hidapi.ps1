[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$BuildRoot = (Join-Path $env:TEMP 'powertray-hidapi-native-build'),
    [string]$CMakePath = (Join-Path $HOME '.codex/tools/cmake-4.4.0-windows-x86_64/bin/cmake.exe')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedCMakeVersion = '4.4.0'
$expectedVsInstallationVersion = '17.14.37411.7'
$sourceManifestPath = Join-Path $PSScriptRoot 'source.json'
$source = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $CMakePath -PathType Leaf)) {
    throw "Pinned CMake executable not found: $CMakePath"
}

$cmakeVersionLine = (& $CMakePath --version | Select-Object -First 1)
$cmakeVersion = ($cmakeVersionLine -replace '^cmake version\s+', '').Trim()
if ($cmakeVersion -ne $expectedCMakeVersion) {
    throw "CMake version mismatch. Expected $expectedCMakeVersion, received $cmakeVersion."
}

$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWhere -PathType Leaf)) {
    throw "Visual Studio Build Tools discovery utility not found: $vsWhere"
}

$vsInstall = & $vsWhere -latest -products Microsoft.VisualStudio.Product.BuildTools -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | ConvertFrom-Json
if (-not $vsInstall) {
    throw 'Visual Studio Build Tools with the x64 MSVC toolset is not installed.'
}

$installationVersion = [string]$vsInstall.installationVersion
if ($installationVersion -ne $expectedVsInstallationVersion) {
    throw "Visual Studio Build Tools version mismatch. Expected $expectedVsInstallationVersion, received $installationVersion."
}

$vsPath = [string]$vsInstall.installationPath
$toolsetVersionFile = Join-Path $vsPath 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt'
$msvcToolsetVersion = (Get-Content -LiteralPath $toolsetVersionFile -Raw).Trim()
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Lib'
$windowsSdkVersion = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um/x64/hid.lib') } |
    Sort-Object { [Version]$_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty Name
if (-not $windowsSdkVersion) {
    throw 'No Windows SDK with x64 hid.lib was found.'
}

if (Test-Path -LiteralPath $BuildRoot) {
    $resolvedBuildRoot = [IO.Path]::GetFullPath($BuildRoot)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
    if (-not $resolvedBuildRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean build root outside TEMP: $resolvedBuildRoot"
    }
    Remove-Item -LiteralPath $resolvedBuildRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
$archivePath = Join-Path $BuildRoot 'hidapi-source.zip'
Invoke-WebRequest -Uri $source.archiveUrl -OutFile $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($archiveHash -ne ([string]$source.archiveSha256).ToUpperInvariant()) {
    throw "Source archive SHA-256 mismatch. Expected $($source.archiveSha256), received $archiveHash."
}

$sourceRoot = Join-Path $BuildRoot 'source'
Expand-Archive -LiteralPath $archivePath -DestinationPath $sourceRoot
$sourceDirectories = @(Get-ChildItem -LiteralPath $sourceRoot -Directory)
if ($sourceDirectories.Count -ne 1) {
    throw "Expected exactly one extracted source directory, found $($sourceDirectories.Count)."
}
$sourceDirectory = $sourceDirectories[0].FullName
$buildDirectory = Join-Path $BuildRoot 'build'

$compilerFlags = "/O2 /Ob2 /DNDEBUG /Brepro /experimental:deterministic /pathmap:$sourceDirectory=hidapi"
$linkerFlags = '/INCREMENTAL:NO /OPT:REF /OPT:ICF /Brepro'
$configureArguments = @(
    '-S', $sourceDirectory,
    '-B', $buildDirectory,
    '-G', 'Visual Studio 17 2022',
    '-A', 'x64',
    '-DBUILD_SHARED_LIBS=ON',
    '-DHIDAPI_WITH_TESTS=OFF',
    '-DHIDAPI_BUILD_HIDTEST=OFF',
    '-DHIDAPI_BUILD_PP_DATA_DUMP=OFF',
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    "-DCMAKE_C_FLAGS_RELEASE=$compilerFlags",
    "-DCMAKE_SHARED_LINKER_FLAGS_RELEASE=$linkerFlags"
)

Invoke-Checked -FilePath $CMakePath -Arguments $configureArguments
Invoke-Checked -FilePath $CMakePath -Arguments @('--build', $buildDirectory, '--config', 'Release', '--target', 'hidapi_winapi', '--parallel')

$builtDlls = @(Get-ChildItem -LiteralPath $buildDirectory -Filter 'hidapi.dll' -File -Recurse)
if ($builtDlls.Count -ne 1) {
    throw "Expected exactly one built hidapi.dll, found $($builtDlls.Count)."
}
$builtDll = $builtDlls[0]
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputDll = Join-Path $OutputDirectory 'hidapi.dll'
Copy-Item -LiteralPath $builtDll.FullName -Destination $outputDll -Force

$evidence = [ordered]@{
    sourceRepository = [string]$source.repository
    sourceCommit = [string]$source.commit
    sourceArchiveSha256 = $archiveHash
    binarySha256 = (Get-FileHash -LiteralPath $outputDll -Algorithm SHA256).Hash.ToUpperInvariant()
    binarySize = (Get-Item -LiteralPath $outputDll).Length
    architecture = 'x64'
    configuration = 'Release'
    generator = 'Visual Studio 17 2022'
    visualStudioBuildTools = $installationVersion
    msvcToolset = $msvcToolsetVersion
    windowsSdk = $windowsSdkVersion
    cmake = $cmakeVersion
    crt = 'MultiThreaded (/MT, static CRT)'
    compilerFlags = $compilerFlags
    linkerFlags = $linkerFlags
    localPatches = @($source.localPatches)
}

$evidencePath = Join-Path $OutputDirectory 'build-evidence.json'
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $evidencePath -Encoding utf8NoBOM
Write-Host "hidapi.dll: $($evidence.binarySha256)"
Write-Host "Build evidence: $evidencePath"
