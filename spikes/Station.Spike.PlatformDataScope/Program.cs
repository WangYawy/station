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
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M22 Spike: 平台组织数据权限（上级看下级、同级隔离；admin 全量）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5109;
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

// 准备平台数据：部门树 + 用户/角色 + 站 + 报警
using (var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    db.Ado.ExecuteCommand("delete from platform_alert_report");
    db.Ado.ExecuteCommand("delete from platform_station");
    db.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    db.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    db.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    db.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");

    var id = new SnowflakeIdGenerator();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
    var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1 };
    db.Insertable(team1).ExecuteCommand();
    db.Insertable(grp1).ExecuteCommand();

    var zhangSan = new User { Id = id.NextId(), UserNo = "zhangsan", Name = "张三", DeptId = grp1.Id };
    var liSi = new User { Id = id.NextId(), UserNo = "lisi", Name = "李四", DeptId = team1.Id };
    db.Insertable(zhangSan).ExecuteCommand();
    db.Insertable(liSi).ExecuteCommand();

    var operatorRole = db.Queryable<Role>().Where(r => r.Code == "operator").First();
    var managerRole = db.Queryable<Role>().Where(r => r.Code == "manager").First();
    db.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRole.Id }).ExecuteCommand();
    db.Insertable(new UserRole { Id = id.NextId(), UserId = liSi.Id, RoleId = managerRole.Id }).ExecuteCommand();

    var hasher = new Sm3PasswordHasher();
    db.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id }).ExecuteCommand();
    db.Insertable(new Account { Id = id.NextId(), UserName = "lisi", PasswordHash = hasher.Hash("Test@123"), UserId = liSi.Id }).ExecuteCommand();

    var stationA = new PlatformStation { Id = id.NextId(), StationCode = "ST-A", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = team1.Id, RegisteredAt = DateTime.Now };
    var stationB = new PlatformStation { Id = id.NextId(), StationCode = "ST-B", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = grp1.Id, RegisteredAt = DateTime.Now };
    db.Insertable(stationA).ExecuteCommand();
    db.Insertable(stationB).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationA.Id, DeptId = team1.Id, LocalAlertId = 1, Type = AlertType.UsbFault, Level = AlertLevel.Warning, Source = "ST-A", Message = "一队报警", OccurredAt = DateTime.Now, ReceivedAt = DateTime.Now }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationB.Id, DeptId = grp1.Id, LocalAlertId = 1, Type = AlertType.NetworkDown, Level = AlertLevel.Critical, Source = "ST-B", Message = "一组报警", OccurredAt = DateTime.Now, ReceivedAt = DateTime.Now }).ExecuteCommand();
}

try
{
    async Task<(HttpClient Client, int StationCount, int AlertCount)> LoginAndQuery(string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        login.EnsureSuccessStatusCode();
        var stations = await http.GetFromJsonAsync<AdminListResponse>("/api/v1/stations?page=1&size=50");
        var alerts = await http.GetFromJsonAsync<AdminListResponse>("/api/v1/alerts?page=1&size=50");
        return (http, (int)stations!.Data!.TotalCount, (int)alerts!.Data!.TotalCount);
    }

    // ---------- 1. admin 全量 ----------
    try
    {
        var (_, stationCount, alertCount) = await LoginAndQuery("admin", "Admin@123");
        Pass("admin全量", stationCount == 2 && alertCount == 2,
            $"站={stationCount}, 报警={alertCount}");
    }
    catch (Exception ex)
    {
        Pass("admin全量", false, ex.Message);
    }

    // ---------- 2. 操作员@一组：仅见本组 ----------
    try
    {
        var (_, stationCount, alertCount) = await LoginAndQuery("zhangsan", "Test@123");
        Pass("操作员仅本组", stationCount == 1 && alertCount == 1,
            $"站={stationCount}, 报警={alertCount}");
    }
    catch (Exception ex)
    {
        Pass("操作员仅本组", false, ex.Message);
    }

    // ---------- 3. 部门负责人@一队：本部门及下级（一组） ----------
    try
    {
        var (_, stationCount, alertCount) = await LoginAndQuery("lisi", "Test@123");
        Pass("负责人本部门及下级", stationCount == 2 && alertCount == 2,
            $"站={stationCount}, 报警={alertCount}");
    }
    catch (Exception ex)
    {
        Pass("负责人本部门及下级", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_station");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ 平台组织数据权限验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record AdminListResponse(bool Success, int Code, string Message, AdminPage? Data);

internal sealed record AdminPage(int PageIndex, int PageSize, long TotalCount, List<object>? Items);
