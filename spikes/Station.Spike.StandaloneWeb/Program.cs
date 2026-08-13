using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M37 Spike: 单机版内置 Web（登录/RBAC/数据隔离/文件查询/审计导出/前端托管）
// ---------------------------------------------------------------------------

const int WebPort = 5124;
Environment.CurrentDirectory = @"E:\Reny\station\src\Station.Desktop\Station.Desktop.WebHost";
var testRoot = Path.Combine(Path.GetTempPath(), "station-m37-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var dbFile = Path.Combine(testRoot, "station.db");

Environment.SetEnvironmentVariable("STATION__WEB__PORT", WebPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");
Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

using var host = HostBuilderFactory.Create().Build();
await host.StartAsync();

// 等 Web 就绪
var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    try
    {
        using var probe = new TcpClient();
        probe.Connect("127.0.0.1", WebPort);
        break;
    }
    catch
    {
        await Task.Delay(300);
    }
}

// 播种测试数据（AuthSeeder 已建 admin/角色/ROOT）
using (var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = $"Data Source={dbFile}",
    DbType = DbType.Sqlite,
    IsAutoCloseConnection = true
}))
{
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

    var taskA = new CollectTask { Id = id.NextId(), TaskNo = "CT-1", RecorderName = "记录仪A", RecorderSerial = "R-A", Protocol = (int)Station.Contracts.ProtocolType.Ums, OperatorUserId = liSi.Id, DeptId = team1.Id, Status = CollectTaskStatus.Completed, IsAuto = true, CreatedAt = DateTime.Now };
    var taskB = new CollectTask { Id = id.NextId(), TaskNo = "CT-2", RecorderName = "记录仪B", RecorderSerial = "R-B", Protocol = (int)Station.Contracts.ProtocolType.Ums, OperatorUserId = zhangSan.Id, DeptId = grp1.Id, Status = CollectTaskStatus.Completed, IsAuto = true, CreatedAt = DateTime.Now };
    db.Insertable(taskA).ExecuteCommand();
    db.Insertable(taskB).ExecuteCommand();
    db.Insertable(new CollectFile { Id = id.NextId(), TaskId = taskA.Id, FileName = "a.mp4", Extension = "mp4", RelativePath = "a.mp4", Size = 1024, Fingerprint = "f1", Status = CollectFileStatus.Completed, CollectedAt = DateTime.Now, Sm3 = "s1", FileNo = "ST0001-1", SyncStatus = UploadStatus.Uploaded }).ExecuteCommand();
    db.Insertable(new CollectFile { Id = id.NextId(), TaskId = taskB.Id, FileName = "b.mp4", Extension = "mp4", RelativePath = "b.mp4", Size = 2048, Fingerprint = "f2", Status = CollectFileStatus.Completed, CollectedAt = DateTime.Now, Sm3 = "s2", FileNo = "ST0001-2", SyncStatus = UploadStatus.Uploaded }).ExecuteCommand();
}

static async Task<HttpClient> Login(int port, string user, string pass)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
    if (!login.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"登录失败 {(int)login.StatusCode}");
    }

    return http;
}

try
{
    // ---------- 0. 未登录 -> 401；首页为 Vue 入口 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{WebPort}") };
        var filesResp = await anon.GetAsync("/api/v1/files?page=1&size=10");
        var html = await (await anon.GetAsync("/")).Content.ReadAsStringAsync();
        Pass("未登录401+Vue首页", filesResp.StatusCode == HttpStatusCode.Unauthorized &&
                                 html.Contains("<div id=\"app\">"),
            $"files={(int)filesResp.StatusCode}, Vue入口={html.Contains("<div id=\"app\">")}");
    }
    catch (Exception ex)
    {
        Pass("未登录401+Vue首页", false, ex.Message);
    }

    // ---------- 1. admin 全量文件 ----------
    try
    {
        using var admin = await Login(WebPort, "admin", "Admin@123");
        var files = await admin.GetFromJsonAsync<PageResponse>("/api/v1/files?page=1&size=20");
        var depts = await admin.GetFromJsonAsync<ListResponse>("/api/v1/depts");
        var users = await admin.GetFromJsonAsync<ListResponse>("/api/v1/users");
        var roles = await admin.GetFromJsonAsync<ListResponse>("/api/v1/roles");
        Pass("admin全量", files!.Data!.TotalCount == 2 &&
                          ((System.Text.Json.JsonElement)depts!.Data!).GetArrayLength() == 3 &&
                          ((System.Text.Json.JsonElement)users!.Data!).GetArrayLength() == 3,
            $"文件={files.Data.TotalCount}, 部门={((System.Text.Json.JsonElement)depts!.Data!).GetArrayLength()}, 用户={((System.Text.Json.JsonElement)users!.Data!).GetArrayLength()}, 角色={((System.Text.Json.JsonElement)roles!.Data!).GetArrayLength()}");
    }
    catch (Exception ex)
    {
        Pass("admin全量", false, ex.ToString());
    }

    // ---------- 2. 数据隔离：负责人见本部门及下级；操作员仅本人 ----------
    try
    {
        using var manager = await Login(WebPort, "lisi", "Test@123");
        var managerFiles = await manager.GetFromJsonAsync<PageResponse>("/api/v1/files?page=1&size=20");
        using var op = await Login(WebPort, "zhangsan", "Test@123");
        var opFiles = await op.GetFromJsonAsync<PageResponse>("/api/v1/files?page=1&size=20");
        Pass("数据隔离", managerFiles!.Data!.TotalCount == 2 && opFiles!.Data!.TotalCount == 1,
            $"负责人={managerFiles.Data.TotalCount}, 操作员={opFiles.Data.TotalCount}");
    }
    catch (Exception ex)
    {
        Pass("数据隔离", false, ex.Message);
    }

    // ---------- 3. 权限：操作员访问部门管理 403 ----------
    try
    {
        using var op = await Login(WebPort, "zhangsan", "Test@123");
        var deptsResp = await op.GetAsync("/api/v1/depts");
        Pass("操作员403", deptsResp.StatusCode == HttpStatusCode.Forbidden, $"HTTP {(int)deptsResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("操作员403", false, ex.Message);
    }

    // ---------- 4. 审计日志查询与导出（来源IP脱敏） ----------
    try
    {
        using var admin = await Login(WebPort, "admin", "Admin@123");
        var logs = await admin.GetFromJsonAsync<PageResponse>("/api/v1/audit-logs?page=1&size=20");
        var export = await admin.GetAsync("/api/v1/audit-logs/export");
        var csv = await export.Content.ReadAsStringAsync();
        Pass("审计查询与导出", export.StatusCode == HttpStatusCode.OK &&
                             csv.Contains("来源IP(脱敏)") && csv.Contains("127.0.0.*") && !csv.Contains("127.0.0.1"),
            $"查询={logs!.Data!.TotalCount}条, 导出含掩码={csv.Contains("127.0.0.*")}");
    }
    catch (Exception ex)
    {
        Pass("审计查询与导出", false, ex.Message);
    }

    // ---------- 5. 记录仪列表与报警 ----------
    try
    {
        using var admin = await Login(WebPort, "admin", "Admin@123");
        var recordersResp = await admin.GetAsync("/api/v1/recorders");
        var alertsResp = await admin.GetAsync("/api/v1/alerts?count=50");
        Pass("记录仪与报警接口", recordersResp.StatusCode == HttpStatusCode.OK && alertsResp.StatusCode == HttpStatusCode.OK,
            $"recorders={(int)recordersResp.StatusCode}, alerts={(int)alertsResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("记录仪与报警接口", false, ex.Message);
    }
}
finally
{
    await host.StopAsync();
    host.Dispose();
    try
    {
        Directory.Delete(testRoot, true);
    }
    catch
    {
    }
}

Console.WriteLine();
Console.WriteLine("================ M37 单机版内置 Web ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record PageResponse(bool Success, int Code, string Message, PageData? Data);

internal sealed record PageData(int PageIndex, int PageSize, long TotalCount, List<object>? Items);

internal sealed record ListResponse(bool Success, int Code, string Message, object? Data);
