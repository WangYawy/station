param(
    [switch]$SkipInstall
)

<#
Builds station-web (pnpm workspace) and verifies the built assets landed in the
desktop WebHost wwwroot, so the next desktop publish embeds the freshest
frontend. Vite writes directly to WebHost\wwwroot (build.outDir), which is
tracked in git; after running this script the changed assets should be
committed together with the frontend change.

Requires pnpm (npm install -g pnpm or corepack enable).

Usage:
  powershell -File scripts/sync-station-web.ps1
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$webDir = Join-Path $root 'station-web'
$wwwrootDir = Join-Path $root 'src\Station.Desktop\Station.Desktop.WebHost\wwwroot'

if (-not (Test-Path (Join-Path $webDir 'package.json'))) {
    throw "station-web not found: $webDir"
}
if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    throw 'pnpm not found. Install it first: npm install -g pnpm (or corepack enable)'
}

Push-Location $webDir
try {
    if (-not $SkipInstall) {
        pnpm install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'pnpm install failed' }
    }
    pnpm run build
    if ($LASTEXITCODE -ne 0) { throw 'pnpm run build failed' }
}
finally {
    Pop-Location
}

if (-not (Test-Path (Join-Path $wwwrootDir 'index.html'))) {
    throw "station-web build output missing index.html under $wwwrootDir"
}

Write-Host "Synced station-web build -> $wwwrootDir"
