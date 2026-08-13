using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SqlSugar;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M31 Spike: 记录仪生命周期预警 + 使用轨迹
//  - 预警：未绑定 / 非白名单 / 长期未使用（可筛选）
//  - 轨迹：近 N 天按日使用量 + 按采集站聚合（数据范围过滤）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5120;
const long Mb = 1024 * 1024;
long stationAId;
long stationBId;
long recorderR001Id;
long recorderIdleId;

using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    if (seed.DbMaintenance.IsAnyTable("platform_recorder"))
    {
        seed.Ado.ExecuteCommand("delete from platform_recorder");
    }

    seed.Ado.ExecuteCommand("delete from platform_file_metadata");
    seed.Ado.ExecuteCommand("delete from platform_command");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST-B')");
    seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'recorder.%'");
    seed.Ado.ExecuteCommand("delete from station_role_permission where RoleId in (select Id from station_role where Code='recorder_view')");
    seed.Ado.ExecuteCommand("delete from station_role where Code='recorder_view'");
    seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu')");
    seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu'))");
    seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu')");
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

    // 自定义角色：仅本部门数据 + recorder:view，用于验证轨迹的数据范围
    var recorderViewPerm = seed.Queryable<Permission>().Where(p => p.Code == "recorder:view").First();
    var recorderViewRole = new Role { Id = id.NextId(), Code = "recorder_view", Name = "记录仪查看", DataScope = DataScope.Dept, IsSystem = false, IsActive = true };
    seed.Insertable(recorderViewRole).ExecuteCommand();
    seed.Insertable(new RolePermission { Id = id.NextId(), RoleId = recorderViewRole.Id, PermissionId = recorderViewPerm.Id }).ExecuteCommand();
    var wangwu = new User { Id = id.NextId(), UserNo = "wangwu", Name = "王五", DeptId = grp1.Id };
    seed.Insertable(wangwu).ExecuteCommand();
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = wangwu.Id, RoleId = recorderViewRole.Id }).ExecuteCommand();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "wangwu", PasswordHash = hasher.Hash("Test@123"), UserId = wangwu.Id }).ExecuteCommand();

    stationAId = id.NextId();
    stationBId = id.NextId();
    seed.Insertable(new PlatformStation { Id = stationAId, StationCode = "ST-A", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = team1.Id, RegisteredAt = DateTime.Now }).ExecuteCommand();
    seed.Insertable(new PlatformStation { Id = stationBId, StationCode = "ST-B", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = grp1.Id, RegisteredAt = DateTime.Now }).ExecuteCommand();
}

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

static async Task<HttpClient> Login(int port, string user, string pass)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
    login.EnsureSuccessStatusCode();
    return http;
}

static async Task<JsonElement> GetJson(HttpClient http, string url)
{
    var resp = await http.GetAsync(url);
    resp.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
}

try
{
    // 上报产生台账：R-001 今日2文件@ST-A + 昨日1文件@ST-B；R-IDLE 今日1文件@ST-A
    using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") })
    {
        var now = DateTime.Now;
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 1L, fileNo = "ST-A-1", fileName = "a.mp4", size = 2 * Mb, kind = 0, sm3 = "s1", collectedAt = now, recorderSerial = "R-001" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 2L, fileNo = "ST-A-2", fileName = "b.mp4", size = 3 * Mb, kind = 0, sm3 = "s2", collectedAt = now, recorderSerial = "R-001" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationBId}/files/metadata", new { stationId = stationBId, localFileId = 3L, fileNo = "ST-B-1", fileName = "c.wav", size = 4 * Mb, kind = 1, sm3 = "s3", collectedAt = now.AddDays(-1), recorderSerial = "R-001" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 4L, fileNo = "ST-A-3", fileName = "d.mp4", size = 1 * Mb, kind = 0, sm3 = "s4", collectedAt = now, recorderSerial = "R-IDLE" });
    }

    using (var admin = await Login(PlatformPort, "admin", "Admin@123"))
    {
        var page = await GetJson(admin, "/api/v1/recorders?page=1&size=50");
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        recorderR001Id = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-001").GetProperty("id").GetInt64();
        recorderIdleId = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-IDLE").GetProperty("id").GetInt64();

        // R-001 白名单+绑定（无预警）；R-IDLE 保持非白名单/未绑定，并把末次上报回拨 40 天（长期未使用）
        await admin.PutAsJsonAsync($"/api/v1/recorders/{recorderR001Id}/whitelist", new { isWhitelisted = true });
        await admin.PutAsJsonAsync($"/api/v1/recorders/{recorderR001Id}/bind", new { userNo = "zhangsan" });
        using (var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            db.Ado.ExecuteCommand("update platform_recorder set LastSeenAt=@t where Id=@id",
                new { t = DateTime.Now.AddDays(-40), id = recorderIdleId });
        }
    }

    // ---------- 0. 未登录轨迹 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync($"/api/v1/recorders/{recorderR001Id}/trail");
        Pass("未登录轨迹401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录轨迹401", false, ex.Message);
    }

    // ---------- 1. 生命周期预警：R-001 无、R-IDLE 三项 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var page = await GetJson(admin, "/api/v1/recorders?page=1&size=50");
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var r1 = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-001");
        var idle = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-IDLE");
        var r1Warnings = r1.GetProperty("lifecycleWarnings").EnumerateArray().Select(x => x.GetString()).ToList();
        var idleWarnings = idle.GetProperty("lifecycleWarnings").EnumerateArray().Select(x => x.GetString()).ToList();
        Pass("生命周期预警", r1Warnings.Count == 0 &&
                            idleWarnings.Contains("no_binding") &&
                            idleWarnings.Contains("not_whitelisted") &&
                            idleWarnings.Contains("idle"),
            $"R-001={string.Join(",", r1Warnings)}, R-IDLE={string.Join(",", idleWarnings)}");
    }
    catch (Exception ex)
    {
        Pass("生命周期预警", false, ex.Message);
    }

    // ---------- 2. 预警筛选：仅 R-IDLE ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var page = await GetJson(admin, "/api/v1/recorders?page=1&size=50&warning=true");
        var serials = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("recorderSerial").GetString()).ToList();
        Pass("预警筛选", serials.Count == 1 && serials[0] == "R-IDLE", $"筛选结果={string.Join(",", serials)}");
    }
    catch (Exception ex)
    {
        Pass("预警筛选", false, ex.Message);
    }

    // ---------- 3. 使用轨迹：按日 + 按站 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var trail = await GetJson(admin, $"/api/v1/recorders/{recorderR001Id}/trail?days=3");
        var byDay = trail.GetProperty("data").GetProperty("byDay").EnumerateArray().ToList();
        var byStation = trail.GetProperty("data").GetProperty("byStation").EnumerateArray().ToList();
        var today = byDay[2];
        var yesterday = byDay[1];
        var day2 = byDay[0];
        Pass("使用轨迹", today.GetProperty("fileCount").GetInt64() == 2 &&
                          yesterday.GetProperty("fileCount").GetInt64() == 1 &&
                          day2.GetProperty("fileCount").GetInt64() == 0 &&
                          byStation.Count == 2 &&
                          byStation[0].GetProperty("fileCount").GetInt64() == 2 &&
                          byStation[1].GetProperty("fileCount").GetInt64() == 1,
            $"按日=今日{byDay[2].GetProperty("fileCount").GetInt64()}/昨日{byDay[1].GetProperty("fileCount").GetInt64()}/前2天{byDay[0].GetProperty("fileCount").GetInt64()}, 按站={string.Join(",", byStation.Select(x => $"{x.GetProperty("stationCode").GetString()}:{x.GetProperty("fileCount").GetInt64()}"))}");
    }
    catch (Exception ex)
    {
        Pass("使用轨迹", false, ex.Message);
    }

    // ---------- 4. 轨迹数据范围：操作员仅见本组（ST-B） ----------
    try
    {
        using var viewer = await Login(PlatformPort, "wangwu", "Test@123");
        var trail = await GetJson(viewer, $"/api/v1/recorders/{recorderR001Id}/trail?days=3");
        var byStation = trail.GetProperty("data").GetProperty("byStation").EnumerateArray().ToList();
        var recorders = await GetJson(viewer, "/api/v1/recorders?page=1&size=50");
        var visibleSerials = recorders.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("recorderSerial").GetString()).ToList();
        Pass("轨迹数据范围", byStation.Count == 1 && byStation[0].GetProperty("stationCode").GetString() == "ST-B" &&
                            visibleSerials.Contains("R-001") && !visibleSerials.Contains("R-IDLE"),
            $"可见站={string.Join(",", byStation.Select(x => x.GetProperty("stationCode").GetString()))}, 可见记录仪={string.Join(",", visibleSerials)}");
    }
    catch (Exception ex)
    {
        Pass("轨迹数据范围", false, ex.Message);
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
    clean.Ado.ExecuteCommand("delete from platform_recorder");
    clean.Ado.ExecuteCommand("delete from platform_file_metadata");
    clean.Ado.ExecuteCommand("delete from platform_command");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST-B')");
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'recorder.%'");
    clean.Ado.ExecuteCommand("delete from station_role_permission where RoleId in (select Id from station_role where Code='recorder_view')");
    clean.Ado.ExecuteCommand("delete from station_role where Code='recorder_view'");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M31 记录仪生命周期预警与使用轨迹 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
