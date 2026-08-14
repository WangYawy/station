param(
    [string]$Version = '0.1.0',
    [string]$Rid = 'win-x64',
    [string]$OutDir = 'publish/packages',
    [switch]$SkipWebSync
)

<#
Publishes the desktop app for Windows (win-x64) and builds a WiX v5 MSI installer.

Prerequisites:
  - .NET SDK 8+
  - WiX v5 CLI (installed automatically via dotnet tool)
  - Node.js 20+ (used by sync-station-web.ps1 unless -SkipWebSync is set)

Usage:
  powershell -File scripts/package-windows.ps1 -Version 0.1.0
  powershell -File scripts/package-windows.ps1 -Version 0.1.0 -SkipWebSync   # reuse existing wwwroot

Output:
  publish/packages/Station.Desktop-<version>-win-x64.msi
#>

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be a 3-part numeric version (e.g. 0.1.0), got: $Version"
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Station.Desktop\Station.Desktop.UI\Station.Desktop.UI.csproj'
$wxs = Join-Path $PSScriptRoot 'wix\Station.Desktop.wxs'
$publishDir = Join-Path $root "publish\desktop\$Rid"
$outDir = Join-Path $root $OutDir

if (-not $SkipWebSync) {
    Write-Host '==> Syncing embedded web assets'
    & (Join-Path $PSScriptRoot 'sync-station-web.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'sync-station-web.ps1 failed' }
}

Write-Host "==> Publishing $Rid"
dotnet publish $project -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish $Rid failed" }

# Runtime data left by a previous local run must never ship in the MSI.
$localDb = Join-Path $publishDir 'station.db'
if (Test-Path $localDb) {
    Remove-Item -LiteralPath $localDb -Force
}

Write-Host '==> Ensuring WiX v5 CLI'
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    dotnet tool install --global wix --version 5.0.2
}
else {
    $installed = (& wix --version) -join ''
    if ($installed -notlike '5.0*') {
        dotnet tool update --global wix --version 5.0.2
    }
}
if ($LASTEXITCODE -ne 0) { throw 'failed to install/update WiX v5 CLI' }

New-Item -ItemType Directory -Force $outDir | Out-Null
$msi = Join-Path $outDir "Station.Desktop-$Version-$Rid.msi"
$publishFwd = $publishDir.Replace('\', '/')

Write-Host "==> Building MSI: $msi"
wix build $wxs -d "PublishDir=$publishFwd" -d "Version=$Version" -o $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

Write-Host "MSI: $msi"
