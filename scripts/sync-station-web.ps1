param(
    [switch]$SkipInstall
)

<#
Builds station-web and syncs the dist output into the desktop WebHost wwwroot
folder, so the next desktop publish embeds the freshest frontend.

The desktop publish copies WebHost\wwwroot into the output (see
Station.Desktop.WebHost.csproj). WebHost\wwwroot is tracked in git, so after
running this script the changed assets should be committed together with the
frontend change.

Usage:
  powershell -File scripts/sync-station-web.ps1
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$webDir = Join-Path $root 'station-web'
$distDir = Join-Path $webDir 'dist'
$wwwrootDir = Join-Path $root 'src\Station.Desktop\Station.Desktop.WebHost\wwwroot'

if (-not (Test-Path (Join-Path $webDir 'package.json'))) {
    throw "station-web not found: $webDir"
}

Push-Location $webDir
try {
    if (-not $SkipInstall) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
}
finally {
    Pop-Location
}

if (-not (Test-Path (Join-Path $distDir 'index.html'))) {
    throw "station-web build output missing index.html under $distDir"
}

# Replace the old hashed assets to avoid stale files accumulating.
$assetsDir = Join-Path $wwwrootDir 'assets'
if (Test-Path $assetsDir) {
    Remove-Item -LiteralPath $assetsDir -Recurse -Force
}
Copy-Item -Path (Join-Path $distDir '*') -Destination $wwwrootDir -Recurse -Force

Write-Host "Synced station-web dist -> $wwwrootDir"
