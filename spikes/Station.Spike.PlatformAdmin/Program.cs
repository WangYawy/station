using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Alerts;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Reporting;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M20 Spike: 平台报警/授权管理查询（注册→激活→心跳→报警上报→查询 API）
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\admin-test";
var dbFile = Path.Combine(testRoot, "station.db");
if (Directory.Exists(testRoot))
{
    Directory.Delete(testRoot, true);
}

Directory.CreateDirectory(testRoot);

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var (licensePrivate, licensePublic) = Sm2LicenseSigner.CreateKeyPair();

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5107");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-ADM");
Environment.SetEnvironmentVariable("STATION__LICENSE__PRIVATEKEYPEM", licensePrivate);
Environment.SetEnvironmentVariable("STATION__LICENSE__PUBLICKEYPEM", licensePublic);
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5107;
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
        ["STATION__DB__CONNECTIONSTRING"] = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None"
    }
});

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

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-ADM'");
}

var host = HostBuilderFactory.Create().Build();
try
{
    await host.StartAsync();

    var context = host.Services.GetRequiredService<IStationContext>();
    var registerDeadline = DateTime.Now.AddSeconds(30);
    while (context.StationId is null && DateTime.Now < registerDeadline)
    {
        await Task.Delay(300);
    }

    var stationId = context.StationId!.Value;
    var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
    var alerts = host.Services.GetRequiredService<IAlertService>();
    var license = host.Services.GetRequiredService<ILicenseService>();
    using var http = new HttpClient();

    // ---------- 1. 激活授权 → 心跳上报 ----------
    try
    {
        var file = new LicenseGenerator(
            host.Services.GetRequiredService<LicenseOptions>())
            .Generate("ST-ADM", host.Services.GetRequiredService<IMachineFingerprintProvider>().CollectFingerprint(),
                DateTime.Now.AddDays(30));
        var (ok, message) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));
        var check = await license.CheckAsync();
        var report = new LicenseStatusReport
        {
            StationId = stationId,
            LicenseStatus = check.Status,
            ExpiresAt = check.ExpiresAt,
            DaysLeft = check.DaysLeft
        };
        await outbox.EnqueueAsync("license-status", System.Text.Json.JsonSerializer.Serialize(report));
        var sent = await outbox.DrainAsync();
        Pass("授权心跳上报", ok && sent == 1 && check.Status == LicenseStatus.Activated,
            $"激活={message}, 上报={sent}, 状态={check.Status}, 剩余={check.DaysLeft}天");
    }
    catch (Exception ex)
    {
        Pass("授权心跳上报", false, ex.Message);
    }

    // ---------- 2. 报警上报 ×2 ----------
    try
    {
        await alerts.WriteAsync(new Alert { Type = AlertType.UnauthorizedAccess, Level = AlertLevel.Critical, Title = "非授权接入", Detail = "SIM-ROGUE 拒绝", Source = "SIM-ROGUE" });
        await alerts.WriteAsync(new Alert { Type = AlertType.BindingInvalid, Level = AlertLevel.Warning, Title = "绑定异常", Detail = "SIM-R1 篡改", Source = "SIM-R1" });
        var sent = await outbox.DrainAsync();
        Pass("报警上报", sent == 2, $"上报={sent}");
    }
    catch (Exception ex)
    {
        Pass("报警上报", false, ex.Message);
    }

    // ---------- 3. 平台查询 API ----------
    try
    {
        var alertsHttp = await http.GetAsync($"http://127.0.0.1:{PlatformPort}/api/v1/alerts?page=1&size=50");
        var alertsResp = await alertsHttp.Content.ReadFromJsonAsync<AdminListResponse>();
        var stationsHttp = await http.GetAsync($"http://127.0.0.1:{PlatformPort}/api/v1/stations?page=1&size=50");
        var stationsResp = await stationsHttp.Content.ReadFromJsonAsync<AdminListResponse>();

        var station = stationsResp!.Data!.Items!.FirstOrDefault(s => s.StationCode == "ST-ADM");
        var ok = alertsResp!.Data!.TotalCount == 2
                 && station is { LicenseStatus: 2 }; // 2 = Activated
        Pass("平台查询API", ok,
            $"报警总数={alertsResp.Data.TotalCount}, 站授权状态={station?.LicenseStatus}, 剩余={station?.LicenseDaysLeft}天");
    }
    catch (Exception ex)
    {
        Pass("平台查询API", false, ex.Message);
    }

    // ---------- 4. 清理 ----------
    try
    {
        var db = host.Services.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.DropTable<SyncOutbox>();
        db.DbMaintenance.DropTable<LicenseInfo>();
        Pass("清理", true, "已删除采集站测试表");
    }
    catch (Exception ex)
    {
        Pass("清理", false, ex.Message);
    }
}
finally
{
    await host.StopAsync();
    host.Dispose();
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine("================ 平台报警/授权管理验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record AdminListResponse(bool Success, int Code, string Message, AdminPage? Data);

internal sealed record AdminPage(int PageIndex, int PageSize, long TotalCount, List<AdminItem>? Items);

internal sealed record AdminItem(
    long Id, long StationId, int Type, int Level, int Status, string Source, string Message,
    DateTime OccurredAt, DateTime ReceivedAt,
    string StationCode, string OsVersion, string CpuArch, string SoftwareVersion,
    int LicenseStatus, DateTime? LicenseExpiresAt, int LicenseDaysLeft, DateTime RegisteredAt);
