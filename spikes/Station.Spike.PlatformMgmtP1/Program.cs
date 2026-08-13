using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using SqlSugar;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M44 Spike: 采集站维修/报废状态 + 记录仪/采集站 CSV 导入
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5130;
long stationAId;
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

    using (var seed = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = platformMysql,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true
    }))
    {
        seed.Ado.ExecuteCommand("delete from platform_recorder where RecorderSerial like 'R-1%'");
        seed.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST2001')");
        seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('station.status','import.stations','import.recorders')");
        seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
        seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
        seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
        seed.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");

        var id = new SnowflakeIdGenerator();
        var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
        var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1 };
        seed.Insertable(team1).ExecuteCommand();
        seed.Insertable(grp1).ExecuteCommand();

        var zhangSan = new User { Id = id.NextId(), UserNo = "zhangsan", Name = "张三", DeptId = grp1.Id };
        var liSi = new User { Id = id.NextId(), UserNo = "lisi", Name = "李四", DeptId = team1.Id };
        seed.Insertable(zhangSan).ExecuteCommand();
        seed.Insertable(liSi).ExecuteCommand();
        var operatorRole = seed.Queryable<Role>().Where(r => r.Code == "operator").First();
        var managerRole = seed.Queryable<Role>().Where(r => r.Code == "manager").First();
        seed.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRole.Id }).ExecuteCommand();
        seed.Insertable(new UserRole { Id = id.NextId(), UserId = liSi.Id, RoleId = managerRole.Id }).ExecuteCommand();
        var hasher = new Sm3PasswordHasher();
        seed.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id }).ExecuteCommand();
        seed.Insertable(new Account { Id = id.NextId(), UserName = "lisi", PasswordHash = hasher.Hash("Test@123"), UserId = liSi.Id }).ExecuteCommand();

        stationAId = id.NextId();
        seed.Insertable(new PlatformStation
        {
            Id = stationAId,
            StationCode = "ST-A",
            CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac",
            OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0",
            DeptId = team1.Id, RegisteredAt = DateTime.Now
        }).ExecuteCommand();
    }

    static async Task<HttpClient> Login(int port, string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        login.EnsureSuccessStatusCode();
        return http;
    }

    // ---------- 1. 采集站运行状态 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var set = await admin.PutAsJsonAsync($"/api/v1/stations/{stationAId}/status", new { status = 1 });
        var stations = await (await admin.GetAsync("/api/v1/stations?page=1&size=50")).Content.ReadFromJsonAsync<StationsPage>();
        var stA = stations!.Data!.Items!.First(x => x.StationId == stationAId);
        using var op = await Login(PlatformPort, "zhangsan", "Test@123");
        var denied = await op.PutAsJsonAsync($"/api/v1/stations/{stationAId}/status", new { status = 2 });
        Pass("采集站运行状态", set.StatusCode == HttpStatusCode.OK &&
                             stA.OperationalStatus == 1 &&
                             denied.StatusCode == HttpStatusCode.Forbidden,
            $"admin={(int)set.StatusCode}, status={stA.OperationalStatus}, 操作员={(int)denied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("采集站运行状态", false, ex.ToString());
    }

    // ---------- 2. 记录仪 CSV 导入 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var csv = "序列号,型号,协议,白名单\nR-1001,DSJ-A8,UMS,是\nR-1002,DSJ-B6,UMS,否\nR-1001,重复,UMS,否\n";
        var resp = await admin.PostAsync("/api/v1/imports/recorders", new StringContent(csv, Encoding.UTF8, "text/plain"));
        var result = await resp.Content.ReadFromJsonAsync<ImportResponse>();
        var recorders = await (await admin.GetAsync("/api/v1/recorders?page=1&size=50&keyword=R-100")).Content.ReadFromJsonAsync<RecordersPage>();
        var r1 = recorders!.Data!.Items!.FirstOrDefault(x => x.RecorderSerial == "R-1001");
        Pass("记录仪导入", result!.Data!.Success == 2 && result.Data.Failed == 1 &&
                           r1 is not null && r1.IsWhitelisted,
            $"成功{result.Data.Success}/失败{result.Data.Failed}, R-1001白名单={r1?.IsWhitelisted}");
    }
    catch (Exception ex)
    {
        Pass("记录仪导入", false, ex.ToString());
    }

    // ---------- 3. 采集站 CSV 导入 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var csv = "站编号,系统,架构,版本,部门编码\nST2001,麒麟V10,x86_64,0.1.0,TEAM1\nST2002,坏部门,x86_64,0.1.0,NOPE\n";
        var resp = await admin.PostAsync("/api/v1/imports/stations", new StringContent(csv, Encoding.UTF8, "text/plain"));
        var result = await resp.Content.ReadFromJsonAsync<ImportResponse>();
        var stations = await (await admin.GetAsync("/api/v1/stations?page=1&size=50&keyword=ST2001")).Content.ReadFromJsonAsync<StationsPage>();
        var s2001 = stations!.Data!.Items!.FirstOrDefault(x => x.StationCode == "ST2001");
        Pass("采集站导入", result!.Data!.Success == 1 && result.Data.Failed == 1 && s2001 is not null && s2001.DeptId != null,
            $"成功{result.Data.Success}/失败{result.Data.Failed}, ST2001部门={s2001?.DeptId}");
    }
    catch (Exception ex)
    {
        Pass("采集站导入", false, ex.ToString());
    }

    // ---------- 4. 审计留痕 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var logs = await (await admin.GetAsync("/api/v1/audit-logs?page=1&size=30")).Content.ReadFromJsonAsync<AuditPage>();
        var types = logs!.Data!.Items!.Select(x => x.OperationType).Distinct().ToList();
        Pass("审计留痕", types.Contains("station.status") && types.Contains("import.recorders") && types.Contains("import.stations"),
            string.Join(",", types));
    }
    catch (Exception ex)
    {
        Pass("审计留痕", false, ex.ToString());
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        try
        {
            platform.Kill();
            platform.WaitForExit(5000);
        }
        catch
        {
        }
    }
}

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    clean.Ado.ExecuteCommand("delete from platform_recorder where RecorderSerial like 'R-1%'");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST2001','ST2002')");
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('station.status','import.stations','import.recorders')");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M44 平台管理增强 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record StationsPage(bool Success, int Code, string Message, StationsData? Data);

internal sealed record StationsData(int PageIndex, int PageSize, long TotalCount, List<StationItem>? Items);

internal sealed record StationItem(long StationId, string StationCode, string OsVersion, string CpuArch, string SoftwareVersion, int OperationalStatus, int LicenseStatus, DateTime? LicenseExpiresAt, int LicenseDaysLeft, long? DeptId, DateTime RegisteredAt);

internal sealed record ImportResponse(bool Success, int Code, string Message, ImportData? Data);

internal sealed record ImportData(int Total, int Success, int Failed, List<object>? Errors);

internal sealed record RecordersPage(bool Success, int Code, string Message, RecordersData? Data);

internal sealed record RecordersData(int PageIndex, int PageSize, long TotalCount, List<RecorderItem>? Items);

internal sealed record RecorderItem(long Id, string RecorderSerial, bool IsWhitelisted);

internal sealed record AuditPage(bool Success, int Code, string Message, AuditData? Data);

internal sealed record AuditData(int PageIndex, int PageSize, long TotalCount, List<AuditItem>? Items);

internal sealed record AuditItem(long Id, string? OperatorAccount, string? OperatorName, long? DeptId, string? SourceIp, string OperationType, string? Target, string? Detail, int Result, DateTime CreatedAt);
