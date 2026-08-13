using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Alerts;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Contracts.Reporting;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M19 Spike: 报警/授权状态上报 SM2 签名验签（站私钥签名→平台公钥验签落库）
// ---------------------------------------------------------------------------

var testRoot = @"E:\Reny\station\archive\reporting-test";
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

var (reportingPrivate, reportingPublic) = Sm2LicenseSigner.CreateKeyPair();
var (licensePrivate, licensePublic) = Sm2LicenseSigner.CreateKeyPair();

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", "http://127.0.0.1:5106");
Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-RPT");
Environment.SetEnvironmentVariable("STATION__REPORTING__PRIVATEKEYPEM", reportingPrivate);
Environment.SetEnvironmentVariable("STATION__LICENSE__PRIVATEKEYPEM", licensePrivate);
Environment.SetEnvironmentVariable("STATION__LICENSE__PUBLICKEYPEM", licensePublic);
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

const int PlatformPort = 5106;
var reportingPublicFile = Path.Combine(testRoot, "platform-reporting-public.pem");
File.WriteAllText(reportingPublicFile, reportingPublic);
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort} --Platform:Reporting:PublicKeyPemFile={reportingPublicFile}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
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

// 清理平台测试数据
using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-RPT'");
}

var host = HostBuilderFactory.Create().Build();
try
{
    Console.WriteLine("STAGE: host starting...");
    await host.StartAsync();
    Console.WriteLine("STAGE: host started, waiting register...");

    var context = host.Services.GetRequiredService<IStationContext>();
    var registerDeadline = DateTime.Now.AddSeconds(30);
    while (context.StationId is null && DateTime.Now < registerDeadline)
    {
        await Task.Delay(300);
    }

    var stationId = context.StationId!.Value;
    Console.WriteLine($"STAGE: registered stationId={stationId}");
    using var diagHttp = new HttpClient();

    // 平台验签诊断：手动 POST 一条有效签名报警
    var probe = new AlertReport
    {
        StationId = stationId,
        LocalAlertId = 555,
        Type = AlertType.UsbFault,
        Level = AlertLevel.Warning,
        Source = "DIAG",
        Message = "诊断报警",
        OccurredAt = DateTime.Now,
        Signature = null
    };
    probe = probe with
    {
        Signature = Sm2LicenseSigner.Sign(reportingPrivate, AlertReportSignature.Canonical(probe))
    };
    var diagResp = await diagHttp.PostAsJsonAsync(
        $"http://127.0.0.1:{PlatformPort}/api/v1/stations/{stationId}/alerts", probe);
    var diagBody = await diagResp.Content.ReadAsStringAsync();
    Console.WriteLine($"DIAG alert post status={(int)diagResp.StatusCode} body={diagBody[..Math.Min(300, diagBody.Length)]}");
    var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
    var alerts = host.Services.GetRequiredService<IAlertService>();
    var license = host.Services.GetRequiredService<ILicenseService>();

    // ---------- 1. 授权状态心跳上报（SM2 签名 → 平台落库） ----------
    try
    {
        var file = new LicenseGenerator(
            host.Services.GetRequiredService<LicenseOptions>())
            .Generate("ST-RPT", host.Services.GetRequiredService<IMachineFingerprintProvider>().CollectFingerprint(),
                DateTime.Now.AddDays(30));
        var (ok, message) = await license.ActivateAsync(LicenseFileCodec.Serialize(file));
        var check = await license.CheckAsync();
        var report = new LicenseStatusReport
        {
            StationId = stationId,
            LicenseStatus = check.Status,
            ExpiresAt = check.ExpiresAt,
            DaysLeft = check.DaysLeft,
            Signature = null
        };
        report = report with
        {
            Signature = Sm2LicenseSigner.Sign(reportingPrivate, LicenseStatusReportSignature.Canonical(report))
        };
        await outbox.EnqueueAsync("license-status", System.Text.Json.JsonSerializer.Serialize(report));
        var sent = await outbox.DrainAsync();

        var platformDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        var station = platformDb.Queryable<Station.Platform.Domain.Entities.PlatformStation>()
            .Where(s => s.StationCode == "ST-RPT").First();
        Pass("授权状态上报", ok && sent == 1 && station is { LicenseStatus: LicenseStatus.Activated },
            $"激活={message}, 上报={sent}, 平台授权状态={station?.LicenseStatus}, 剩余={station?.LicenseDaysLeft}天");
        platformDb.Dispose();
    }
    catch (Exception ex)
    {
        Pass("授权状态上报", false, ex.Message);
    }

    // ---------- 2. 报警自动上报（WriteAsync → Outbox → 平台验签落库） ----------
    try
    {
        // 传输一致性诊断：签名→序列化→反序列化→重算 canonical
        var probe = new AlertReport
        {
            StationId = stationId,
            LocalAlertId = 1,
            Type = AlertType.UnauthorizedAccess,
            Level = AlertLevel.Critical,
            Source = "SIM-ROGUE",
            Message = "SIM-ROGUE 拒绝接入",
            OccurredAt = DateTime.Now
        };
        var signedProbe = probe with
        {
            Signature = Sm2LicenseSigner.Sign(reportingPrivate, AlertReportSignature.Canonical(probe))
        };
        var transport = System.Text.Json.JsonSerializer.Deserialize<AlertReport>(
            System.Text.Json.JsonSerializer.Serialize(signedProbe))!;
        var canonicalMatch = AlertReportSignature.Canonical(transport) == AlertReportSignature.Canonical(probe);
        var transportVerify = Sm2LicenseSigner.Verify(
            reportingPublic, AlertReportSignature.Canonical(transport), transport.Signature ?? string.Empty);
        Console.WriteLine($"DIAG canonicalMatch={canonicalMatch} transportVerify={transportVerify} tz={TimeZoneInfo.Local.Id}");

        await alerts.WriteAsync(new Alert
        {
            Type = AlertType.UnauthorizedAccess,
            Level = AlertLevel.Critical,
            Title = "非授权接入",
            Detail = "SIM-ROGUE 拒绝接入",
            Source = "SIM-ROGUE"
        });
        var pending = await outbox.CountPendingAsync();
        var sent = await outbox.DrainAsync();

        var platformDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        var count = platformDb.Queryable<Station.Platform.Domain.Entities.PlatformAlertReport>().Count();
        Pass("报警自动上报", pending == 1 && sent == 1 && count == 1,
            $"入队={pending}, 上报={sent}, 平台报警数={count}");
        platformDb.Dispose();
    }
    catch (Exception ex)
    {
        Pass("报警自动上报", false, ex.Message);
    }

    // ---------- 3. 篡改签名报警被平台拒绝 ----------
    try
    {
        var forged = new AlertReport
        {
            StationId = stationId,
            LocalAlertId = 999,
            Type = AlertType.NetworkDown,
            Level = AlertLevel.Warning,
            Source = "FORGE",
            Message = "伪造报警",
            OccurredAt = DateTime.Now,
            Signature = "invalid-signature"
        };
        await outbox.EnqueueAsync("alert", System.Text.Json.JsonSerializer.Serialize(forged));
        var sent = await outbox.DrainAsync();

        var platformDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        var count = platformDb.Queryable<Station.Platform.Domain.Entities.PlatformAlertReport>().Count();
        var retried = (await host.Services.GetRequiredService<IRepository<SyncOutbox>>()
            .GetListAsync(o => o.Topic == "alert")).First(o => o.Status != 1).RetryCount;
        Pass("篡改报警拒绝", sent == 0 && count == 1 && retried >= 1,
            $"上报成功={sent}(应0), 平台报警数={count}(应1), 队列重试={retried}");
        platformDb.Dispose();
    }
    catch (Exception ex)
    {
        Pass("篡改报警拒绝", false, ex.Message);
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
Console.WriteLine("================ 上报 SM2 签名验签验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
