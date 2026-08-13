using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Station.Contracts.Registration;

// ---------------------------------------------------------------------------
// M48 Spike: 平台 SignalR 实时推送（注册/报警/授权状态事件广播）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
const int PlatformPort = 5160;

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ---------- 0. 清理本次测试站 ----------
using (var seed = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = SqlSugar.DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from platform_alert_report where StationId in (select Id from platform_station where StationCode='ST-RT')");
    seed.Ado.ExecuteCommand("delete from platform_file_metadata where StationId in (select Id from platform_station where StationCode='ST-RT')");
    seed.Ado.ExecuteCommand("delete from platform_command where StationId in (select Id from platform_station where StationCode='ST-RT')");
    seed.Ado.ExecuteCommand("delete from platform_config_change where StationId in (select Id from platform_station where StationCode='ST-RT')");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-RT'");
}

// ---------- 1. 启动平台 ----------
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = platformMysql
    }
});

try
{
    var deadline = DateTime.Now.AddSeconds(30);
    while (DateTime.Now < deadline)
    {
        try
        {
            using var probe = new TcpClient();
            probe.Connect("127.0.0.1", PlatformPort);
            break;
        }
        catch
        {
            await Task.Delay(500);
        }
    }

    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };

    // ---------- 2. 两个客户端连接 Hub ----------
    var clientA = await ConnectHubAsync(PlatformPort);
    var clientB = await ConnectHubAsync(PlatformPort);

    // ---------- 3. 采集站注册 → station.registered ----------
    try
    {
        var receivedA = WaitEventAsync(clientA, "station.registered", 15);
        var receivedB = WaitEventAsync(clientB, "station.registered", 15);
        var resp = await http.PostAsJsonAsync("/api/v1/stations/register", new StationRegistrationRequest
        {
            StationCode = "ST-RT",
            MachineFingerprint = new MachineFingerprint
            {
                CpuSerial = "cpu-rt",
                MotherboardSerial = "mb-rt",
                DiskSerial = "disk-rt",
                MacAddress = "mac-rt"
            },
            OsVersion = "win11",
            CpuArch = "x86_64",
            SoftwareVersion = "0.1.0",
            UsbPortCount = 0,
            StationBaseUrl = "http://127.0.0.1:5299"
        });
        var reg = await resp.Content.ReadFromJsonAsync<RegisterResp>();
        var eA = await receivedA;
        var eB = await receivedB;
        var stationId = reg?.Data?.StationId ?? 0;
        Pass("注册事件广播", resp.IsSuccessStatusCode && eA is { Type: "station.registered" } &&
                            eA.StationId == stationId && eB?.Type == eA.Type,
            $"stationId={stationId}, A={eA?.Type}@{eA?.StationId}, B={eB?.Type}@{eB?.StationId}");
    }
    catch (Exception ex)
    {
        Pass("注册事件广播", false, ex.Message);
    }

    // ---------- 4. 报警上报 → alert.created ----------
    try
    {
        var stationId = await ResolveStationIdAsync(platformMysql);
        var received = WaitEventAsync(clientA, "alert.created", 15);
        var resp = await http.PostAsJsonAsync(
            $"/api/v1/stations/{stationId}/alerts",
            new { localAlertId = 1, type = 4, level = 1, source = "ST-RT", message = "实时推送测试报警", occurredAt = DateTime.Now });
        var e = await received;
        Pass("报警事件推送", resp.IsSuccessStatusCode && e is { Type: "alert.created" } && e.StationId == stationId,
            $"type={e?.Type}, stationId={e?.StationId}");
    }
    catch (Exception ex)
    {
        Pass("报警事件推送", false, ex.Message);
    }

    // ---------- 5. 授权状态上报 → station.license ----------
    try
    {
        var stationId = await ResolveStationIdAsync(platformMysql);
        var received = WaitEventAsync(clientB, "station.license", 15);
        var resp = await http.PostAsJsonAsync(
            $"/api/v1/stations/{stationId}/license",
            new { licenseStatus = 2, expiresAt = DateTime.Now.AddDays(365), daysLeft = 365 });
        var e = await received;
        Pass("授权状态推送", resp.IsSuccessStatusCode && e is { Type: "station.license" } && e.StationId == stationId,
            $"type={e?.Type}, stationId={e?.StationId}");
    }
    catch (Exception ex)
    {
        Pass("授权状态推送", false, ex.Message);
    }

    await clientA.StopAsync();
    await clientB.StopAsync();
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine("================ SignalR 实时推送验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

static async Task<HubConnection> ConnectHubAsync(int port)
{
    var connection = new HubConnectionBuilder()
        .WithUrl($"http://127.0.0.1:{port}/hubs/stations")
        .WithAutomaticReconnect()
        .Build();
    await connection.StartAsync();
    return connection;
}

static Task<RealtimeEvent?> WaitEventAsync(HubConnection connection, string type, int seconds)
{
    var tcs = new TaskCompletionSource<RealtimeEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var reg = connection.On<RealtimeEvent>("station.event", e =>
    {
        if (e.Type == type)
        {
            tcs.TrySetResult(e);
        }
    });
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        tcs.TrySetResult(null);
    });
    return tcs.Task.ContinueWith(t =>
    {
        reg.Dispose();
        return t.Result;
    }, TaskScheduler.Default);
}

static async Task<long> ResolveStationIdAsync(string connectionString)
{
    using var seed = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = SqlSugar.DbType.MySql,
        IsAutoCloseConnection = true
    });
    var id = await seed.Ado.GetLongAsync("select Id from platform_station where StationCode='ST-RT' limit 1");
    return id;
}

internal sealed record RealtimeEvent(string? Type, long? StationId, long? DeptId, DateTime OccurredAt);

internal sealed record RegisterResp(bool Success, int Code, string Message, StationRegData? Data);

internal sealed record StationRegData(long StationId, string StationCode);
