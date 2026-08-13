param(
    [string[]]$Rids = @('win-x64', 'linux-x64', 'linux-arm64')
)

<#
桌面端跨平台单文件发布（信创：海光/兆芯 x86_64 → linux-x64，飞腾/鲲鹏 ARM64 → linux-arm64）
用法：powershell -File scripts/publish-desktop.ps1
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Station.Desktop\Station.Desktop.UI\Station.Desktop.UI.csproj'

foreach ($rid in $Rids) {
    $out = Join-Path $root "publish\desktop\$rid"
    Write-Host "==> Publishing $rid -> $out"
    dotnet publish $project -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $out
    if ($LASTEXITCODE -ne 0) {
        throw "publish $rid failed"
    }
}

Write-Host '完成。产物位于 publish/desktop/<rid>/'
