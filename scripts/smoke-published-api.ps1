param(
    [int]$Port = 5122,
    [string]$PublishDir = 'E:\Reny\station\publish\platform-api'
)

<#
发布产物冒烟测试：启动平台 API（MySQL）→ 验证 Vue 首页与登录/文件接口 → 退出。
用法：powershell -File scripts/smoke-published-api.ps1
#>

$ErrorActionPreference = 'Stop'
$env:STATION__DB__PROVIDER = 'MySql'
$env:STATION__DB__CONNECTIONSTRING = 'Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None'

$dll = Join-Path $PublishDir 'Station.Platform.Api.dll'
$proc = Start-Process -FilePath 'dotnet' -ArgumentList $dll, '--urls', "http://127.0.0.1:$Port" -WorkingDirectory $PublishDir -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 8
    $html = (Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 10).Content
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $login = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/v1/auth/login" -Method Post `
        -Body '{"userName":"admin","password":"Admin@123"}' -ContentType 'application/json' -WebSession $session
    $files = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/v1/files?page=1&size=5" -WebSession $session -UseBasicParsing
    [PSCustomObject]@{
        Vue入口 = $html.Contains('<div id="app">')
        登录用户 = $login.session.userName
        文件接口 = $files.StatusCode
    } | Format-List
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
