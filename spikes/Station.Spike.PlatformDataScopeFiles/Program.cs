using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M23 Spike: 文件模块数据权限 + 采集站部门归属管理
//  - 文件列表/详情按部门树过滤（与站/报警一致），未登录 401
//  - 部门树端点 dept:view；归属修改 station:manage，越权 403
//  - 调整站归属后历史文件/报警保留上报时归属快照
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5111;
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

long stationAId;
long stationBId;
long team1Id;
long grp1Id;
string fileNoA;
string fileNoB;

// 准备平台数据：部门树 + 用户/角色 + 站 + 报警 + 文件
using (var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    db.Ado.ExecuteCommand("delete from platform_file_metadata");
    db.Ado.ExecuteCommand("delete from platform_alert_report");
    db.Ado.ExecuteCommand("delete from platform_station");
    db.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    db.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    db.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    db.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");

    var id = new SnowflakeIdGenerator();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
    var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1 };
    team1Id = team1.Id;
    grp1Id = grp1.Id;
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
    stationAId = stationA.Id;
    stationBId = stationB.Id;

    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationA.Id, DeptId = team1.Id, LocalAlertId = 1, Type = AlertType.UsbFault, Level = AlertLevel.Warning, Source = "ST-A", Message = "一队报警", OccurredAt = DateTime.Now, ReceivedAt = DateTime.Now }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationB.Id, DeptId = grp1.Id, LocalAlertId = 1, Type = AlertType.NetworkDown, Level = AlertLevel.Critical, Source = "ST-B", Message = "一组报警", OccurredAt = DateTime.Now, ReceivedAt = DateTime.Now }).ExecuteCommand();

    var now = DateTime.Now;
    fileNoA = "SPIKE-F-A-001";
    fileNoB = "SPIKE-F-B-001";
    db.Insertable(new PlatformFileMetadata { Id = id.NextId(), StationId = stationA.Id, LocalFileId = 1, FileNo = fileNoA, FileName = "一队视频.mp4", Size = 1024 * 1024, Kind = FileKind.Video, Sm3 = "a", CollectedAt = now, RecorderSerial = "R-A", UserNo = "zhangsan", DeptCode = "TEAM1", DeptId = team1.Id, ReceivedAt = now }).ExecuteCommand();
    db.Insertable(new PlatformFileMetadata { Id = id.NextId(), StationId = stationB.Id, LocalFileId = 1, FileNo = fileNoB, FileName = "一组视频.mp4", Size = 2 * 1024 * 1024, Kind = FileKind.Video, Sm3 = "b", CollectedAt = now, RecorderSerial = "R-B", UserNo = "lisi", DeptCode = "GRP1", DeptId = grp1.Id, ReceivedAt = now }).ExecuteCommand();
}

try
{
    async Task<(HttpClient Client, long StationCount, long AlertCount, long FileCount)> LoginAndQuery(string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        login.EnsureSuccessStatusCode();
        var stations = await http.GetFromJsonAsync<AdminListResponse>("/api/v1/stations?page=1&size=50");
        var alerts = await http.GetFromJsonAsync<AdminListResponse>("/api/v1/alerts?page=1&size=50");
        var files = await http.GetFromJsonAsync<AdminListResponse>("/api/v1/files?page=1&size=100");
        return (http, stations!.Data!.TotalCount, alerts!.Data!.TotalCount, files!.Data!.TotalCount);
    }

    // ---------- 0. 未登录访问文件列表 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync("/api/v1/files?page=1&size=10");
        Pass("未登录401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录401", false, ex.Message);
    }

    // ---------- 1. admin 全量：站2 报警2 文件2 部门3 ----------
    try
    {
        var (_, stationCount, alertCount, fileCount) = await LoginAndQuery("admin", "Admin@123");
        Pass("admin全量", stationCount == 2 && alertCount == 2 && fileCount == 2,
            $"站={stationCount}, 报警={alertCount}, 文件={fileCount}");
    }
    catch (Exception ex)
    {
        Pass("admin全量", false, ex.Message);
    }

    // ---------- 2. 操作员@一组：仅本组数据；部门树无权限；越权详情404 ----------
    try
    {
        var (http, stationCount, alertCount, fileCount) = await LoginAndQuery("zhangsan", "Test@123");
        var depts = await http.GetAsync("/api/v1/depts");
        var outScope = await http.GetAsync($"/api/v1/files/{fileNoA}");
        var inScope = await http.GetAsync($"/api/v1/files/{fileNoB}");
        Pass("操作员仅本组", stationCount == 1 && alertCount == 1 && fileCount == 1,
            $"站={stationCount}, 报警={alertCount}, 文件={fileCount}");
        Pass("操作员部门树403", depts.StatusCode == HttpStatusCode.Forbidden, $"HTTP {(int)depts.StatusCode}");
        Pass("越权详情404", outScope.StatusCode == HttpStatusCode.NotFound, $"HTTP {(int)outScope.StatusCode}");
        Pass("本组详情200", inScope.StatusCode == HttpStatusCode.OK, $"HTTP {(int)inScope.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("操作员场景", false, ex.Message);
    }

    // ---------- 3. 部门负责人@一队：本部门及下级（一组） ----------
    try
    {
        var (_, stationCount, alertCount, fileCount) = await LoginAndQuery("lisi", "Test@123");
        Pass("负责人本部门及下级", stationCount == 2 && alertCount == 2 && fileCount == 2,
            $"站={stationCount}, 报警={alertCount}, 文件={fileCount}");
    }
    catch (Exception ex)
    {
        Pass("负责人本部门及下级", false, ex.Message);
    }

    // ---------- 4. 归属修改：负责人无 station:manage -> 403；admin 可改 ----------
    try
    {
        var manager = await LoginAndQuery("lisi", "Test@123");
        var denied = await manager.Client.PutAsJsonAsync(
            $"/api/v1/stations/{stationBId}/dept", new { deptId = 1 });
        Pass("负责人改归属403", denied.StatusCode == HttpStatusCode.Forbidden, $"HTTP {(int)denied.StatusCode}");

        var admin = await LoginAndQuery("admin", "Admin@123");
        var ok = await admin.Client.PutAsJsonAsync(
            $"/api/v1/stations/{stationBId}/dept", new { deptId = 1 });
        Pass("admin改归属200", ok.StatusCode == HttpStatusCode.OK, $"HTTP {(int)ok.StatusCode}");

        var stations = await admin.Client.GetFromJsonAsync<AdminListResponse>("/api/v1/stations?page=1&size=50");
        var stationB = stations!.Data!.Items!.First(x => x.GetProperty("stationCode").GetString() == "ST-B");
        Pass("ST-B归属已改为总部", stationB.GetProperty("deptId").GetInt64() == 1,
            $"deptId={stationB.GetProperty("deptId").GetInt64()}");

        var after = await LoginAndQuery("zhangsan", "Test@123");
        Pass("历史文件归属快照保留", after.FileCount == 1,
            $"操作员仍见 {after.FileCount} 个文件（ST-B 历史文件仍按上报时 grp1 归属）");

        var filtered = await admin.Client.GetFromJsonAsync<AdminListResponse>($"/api/v1/files?page=1&size=100&deptId={grp1Id}");
        Pass("文件按部门筛选", filtered!.Data!.TotalCount == 1, $"deptId={grp1Id} 命中 {filtered.Data.TotalCount} 个文件");
    }
    catch (Exception ex)
    {
        Pass("归属修改场景", false, ex.Message);
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
    clean.Ado.ExecuteCommand("delete from platform_file_metadata");
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_station");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M23 文件数据权限 + 采集站归属管理 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record AdminListResponse(bool Success, int Code, string Message, AdminPage? Data);

internal sealed record AdminPage(int PageIndex, int PageSize, long TotalCount, List<JsonElement>? Items);
