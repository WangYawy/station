using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M21 Spike: 平台登录认证 + RBAC 报警处置（401/200/403）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5108;
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

// 插平台数据（站 + 报警）与审计员账号
using (var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    db.Ado.ExecuteCommand("delete from platform_alert_report");
    db.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-RBAC'");
    db.Ado.ExecuteCommand("delete from station_account where UserName in ('auditor')");
    db.Ado.ExecuteCommand("delete from station_user where UserNo in ('auditor')");

    var id = new SnowflakeIdGenerator();
    db.Insertable(new PlatformStation
    {
        Id = id.NextId(),
        StationCode = "ST-RBAC",
        CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac",
        OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0",
        RegisteredAt = DateTime.Now
    }).ExecuteCommand();
    var stationId = db.Queryable<PlatformStation>().Where(s => s.StationCode == "ST-RBAC").First().Id;

    db.Insertable(new PlatformAlertReport
    {
        Id = id.NextId(),
        StationId = stationId,
        LocalAlertId = 1,
        Type = AlertType.UnauthorizedAccess,
        Level = AlertLevel.Critical,
        Source = "SIM-ROGUE",
        Message = "非授权接入",
        OccurredAt = DateTime.Now,
        ReceivedAt = DateTime.Now
    }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport
    {
        Id = id.NextId(),
        StationId = stationId,
        LocalAlertId = 2,
        Type = AlertType.BindingInvalid,
        Level = AlertLevel.Warning,
        Source = "SIM-R1",
        Message = "绑定异常",
        OccurredAt = DateTime.Now,
        ReceivedAt = DateTime.Now
    }).ExecuteCommand();

    // 审计员账号（无 alert:handle 权限）
    var auditorRole = db.Queryable<Role>().Where(r => r.Code == "auditor").First();
    var auditorUser = new User { Id = id.NextId(), UserNo = "auditor", Name = "审计员", DeptId = 1 };
    db.Insertable(auditorUser).ExecuteCommand();
    db.Insertable(new UserRole { Id = id.NextId(), UserId = auditorUser.Id, RoleId = auditorRole.Id }).ExecuteCommand();
    db.Insertable(new Account
    {
        Id = id.NextId(),
        UserName = "auditor",
        PasswordHash = new Sm3PasswordHasher().Hash("Test@123"),
        UserId = auditorUser.Id
    }).ExecuteCommand();
}

try
{
    using var adminHandler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    using var adminHttp = new HttpClient(adminHandler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
    using var auditorHandler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    using var auditorHttp = new HttpClient(auditorHandler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };

    // ---------- 1. 未登录 → 401 ----------
    try
    {
        var unauth = await new HttpClient().GetAsync($"http://127.0.0.1:{PlatformPort}/api/v1/alerts?page=1&size=10");
        Pass("未登录401", unauth.StatusCode == HttpStatusCode.Unauthorized,
            $"HTTP={unauth.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录401", false, ex.Message);
    }

    // ---------- 2. admin 登录 → 查询报警 ----------
    try
    {
        var login = await adminHttp.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = "admin", password = "Admin@123" });
        login.EnsureSuccessStatusCode();
        var list = await adminHttp.GetFromJsonAsync<AlertListResponse>("/api/v1/alerts?page=1&size=10");
        Pass("admin登录+查询", list?.Data?.TotalCount == 2,
            $"登录={login.StatusCode}, 报警总数={list?.Data?.TotalCount}");
    }
    catch (Exception ex)
    {
        Pass("admin登录+查询", false, ex.Message);
    }

    // ---------- 3. admin 处置报警 ----------
    try
    {
        var list = await adminHttp.GetFromJsonAsync<AlertListResponse>("/api/v1/alerts?page=1&size=10");
        var alertId = list!.Data!.Items![0].Id;
        var set = await adminHttp.PostAsJsonAsync($"/api/v1/alerts/{alertId}/status", new { status = 1 });
        var after = await adminHttp.GetFromJsonAsync<AlertListResponse>("/api/v1/alerts?page=1&size=10");
        var updated = after!.Data!.Items!.First(a => a.Id == alertId);
        Pass("admin处置", set.StatusCode == HttpStatusCode.OK && updated.Status == 1,
            $"HTTP={set.StatusCode}, 状态={updated.Status}");
    }
    catch (Exception ex)
    {
        Pass("admin处置", false, ex.Message);
    }

    // ---------- 4. 审计员（无权限）处置 → 403 ----------
    try
    {
        var login = await auditorHttp.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = "auditor", password = "Test@123" });
        login.EnsureSuccessStatusCode();
        var list = await auditorHttp.GetFromJsonAsync<AlertListResponse>("/api/v1/alerts?page=1&size=10");
        var alertId = list!.Data!.Items!.Last().Id;
        var set = await auditorHttp.PostAsJsonAsync($"/api/v1/alerts/{alertId}/status", new { status = 2 });
        Pass("审计员403", set.StatusCode == HttpStatusCode.Forbidden,
            $"HTTP={set.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("审计员403", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

// 清理平台测试数据
using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-RBAC'");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('auditor')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo='auditor')");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('auditor')");
}

Console.WriteLine();
Console.WriteLine("================ 平台 RBAC 报警处置验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record AlertListResponse(bool Success, int Code, string Message, AlertPage? Data);

internal sealed record AlertPage(int PageIndex, int PageSize, long TotalCount, List<AlertItem>? Items);

internal sealed record AlertItem(long Id, long StationId, int Type, int Level, int Status, string Source, string Message, DateTime OccurredAt, DateTime ReceivedAt);
