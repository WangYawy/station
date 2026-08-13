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
// M24 Spike: 跨站汇总统计（概览/采集趋势/采集排行/报警统计）
//  - 全部按部门树数据权限过滤（admin 全量 / 操作员本组 / 负责人本部门及下级）
//  - 在线率基于 LastHeartbeatAt（站→平台任意入站请求打点）
//  - 三库 DbMaintenance 补列机制校验（MySQL 实测 + PG/Kingbase 通道验证）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const long Mb = 1024 * 1024;
const int PlatformPort = 5112;
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
long grp1Id;

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

    var now = DateTime.Now;
    var stationA = new PlatformStation { Id = id.NextId(), StationCode = "ST-A", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = team1.Id, LastHeartbeatAt = now, RegisteredAt = now };
    var stationB = new PlatformStation { Id = id.NextId(), StationCode = "ST-B", CpuSerial = "c", MotherboardSerial = "m", DiskSerial = "d", MacAddress = "mac", OsVersion = "win11", CpuArch = "x86_64", SoftwareVersion = "0.1.0", DeptId = grp1.Id, LastHeartbeatAt = now.AddMinutes(-30), RegisteredAt = now };
    db.Insertable(stationA).ExecuteCommand();
    db.Insertable(stationB).ExecuteCommand();
    stationAId = stationA.Id;
    stationBId = stationB.Id;

    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationA.Id, DeptId = team1.Id, LocalAlertId = 1, Type = AlertType.UsbFault, Level = AlertLevel.Warning, Status = AlertStatus.Pending, Source = "ST-A", Message = "USB故障", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationA.Id, DeptId = team1.Id, LocalAlertId = 2, Type = AlertType.DiskLow, Level = AlertLevel.Info, Status = AlertStatus.Closed, Source = "ST-A", Message = "磁盘不足", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationB.Id, DeptId = grp1.Id, LocalAlertId = 1, Type = AlertType.NetworkDown, Level = AlertLevel.Critical, Status = AlertStatus.Pending, Source = "ST-B", Message = "网络中断", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();
    db.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationB.Id, DeptId = grp1.Id, LocalAlertId = 2, Type = AlertType.ChecksumFailed, Level = AlertLevel.Warning, Status = AlertStatus.Processed, Source = "ST-B", Message = "校验失败", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();

    void AddFile(long stationId, long deptId, string no, string name, long size, FileKind kind, DateTime collected)
    {
        db.Insertable(new PlatformFileMetadata
        {
            Id = id.NextId(), StationId = stationId, DeptId = deptId, LocalFileId = id.NextId() % 100000,
            FileNo = no, FileName = name, Size = size, Kind = kind, Sm3 = no, CollectedAt = collected,
            RecorderSerial = no, UserNo = "u1", DeptCode = "D", ReceivedAt = collected
        }).ExecuteCommand();
    }

    AddFile(stationA.Id, team1.Id, "F-A-1", "一队今天1.mp4", 1 * Mb, FileKind.Video, now);
    AddFile(stationA.Id, team1.Id, "F-A-2", "一队今天2.mp4", 2 * Mb, FileKind.Video, now);
    AddFile(stationA.Id, team1.Id, "F-A-3", "一队昨天3.jpg", 3 * Mb, FileKind.Image, now.AddDays(-1));
    AddFile(stationB.Id, grp1.Id, "F-B-1", "一组今天4.mp4", 4 * Mb, FileKind.Video, now);
    AddFile(stationB.Id, grp1.Id, "F-B-2", "一组前3天5.wav", 5 * Mb, FileKind.Audio, now.AddDays(-3));

    Console.WriteLine($"[SEED] 报警={db.Queryable<PlatformAlertReport>().Count()}, 文件={db.Queryable<PlatformFileMetadata>().Count()}, 站={db.Queryable<PlatformStation>().Count()}");
    foreach (var a in db.Queryable<PlatformAlertReport>().ToList())
    {
        Console.WriteLine($"[SEED] 报警 station={a.StationId} dept={a.DeptId} level={(int)a.Level} status={(int)a.Status}");
    }
}

try
{
    async Task<HttpClient> Login(string user, string pass)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
        login.EnsureSuccessStatusCode();
        return http;
    }

    async Task<JsonElement> GetAsync(HttpClient http, string url)
    {
        var resp = await http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    }

    // ---------- 0. 未登录 overview -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync("/api/v1/stats/overview");
        Pass("未登录401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录401", false, ex.Message);
    }

    // ---------- 1. admin 概览 ----------
    try
    {
        using var admin = await Login("admin", "Admin@123");
        var o = (await GetAsync(admin, "/api/v1/stats/overview")).GetProperty("data");
        var ok = o.GetProperty("stationCount").GetInt64() == 2 &&
                 o.GetProperty("onlineCount").GetInt64() == 1 &&
                 o.GetProperty("offlineCount").GetInt64() == 1 &&
                 o.GetProperty("fileCount").GetInt64() == 5 &&
                 o.GetProperty("totalSize").GetInt64() == 15 * Mb &&
                 o.GetProperty("todayFileCount").GetInt64() == 3 &&
                 o.GetProperty("todaySize").GetInt64() == 7 * Mb &&
                 o.GetProperty("videoCount").GetInt64() == 3 &&
                 o.GetProperty("pendingAlertCount").GetInt64() == 2 &&
                 o.GetProperty("alertCount").GetInt64() == 4;
        Pass("admin概览", ok,
            $"站={o.GetProperty("stationCount").GetInt64()}, 在线={o.GetProperty("onlineCount").GetInt64()}, 文件={o.GetProperty("fileCount").GetInt64()}, 容量={o.GetProperty("totalSize").GetInt64()}, 今日={o.GetProperty("todayFileCount").GetInt64()}/{o.GetProperty("todaySize").GetInt64()}, 视频={o.GetProperty("videoCount").GetInt64()}, 报警={o.GetProperty("alertCount").GetInt64()}（待处理={o.GetProperty("pendingAlertCount").GetInt64()}）");
    }
    catch (Exception ex)
    {
        Pass("admin概览", false, ex.Message);
    }

    // ---------- 2. 操作员@一组：仅本组 ----------
    try
    {
        using var op = await Login("zhangsan", "Test@123");
        var o = (await GetAsync(op, "/api/v1/stats/overview")).GetProperty("data");
        var alertItems = (await GetAsync(op, "/api/v1/alerts?page=1&size=50")).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Pass("操作员仅本组", o.GetProperty("stationCount").GetInt64() == 1 &&
                           o.GetProperty("fileCount").GetInt64() == 2 &&
                           o.GetProperty("totalSize").GetInt64() == 9 * Mb &&
                           o.GetProperty("todayFileCount").GetInt64() == 1 &&
                           o.GetProperty("alertCount").GetInt64() == 2 &&
                           o.GetProperty("videoCount").GetInt64() == 1 &&
                           alertItems.Count == 2,
            $"站={o.GetProperty("stationCount").GetInt64()}, 文件={o.GetProperty("fileCount").GetInt64()}, 报警={o.GetProperty("alertCount").GetInt64()}, 报警列表={alertItems.Count}");
    }
    catch (Exception ex)
    {
        Pass("操作员仅本组", false, ex.Message);
    }

    // ---------- 3. 负责人@一队：本部门及下级 ----------
    try
    {
        using var mgr = await Login("lisi", "Test@123");
        var o = (await GetAsync(mgr, "/api/v1/stats/overview")).GetProperty("data");
        Pass("负责人本部门及下级", o.GetProperty("stationCount").GetInt64() == 2 &&
                                 o.GetProperty("fileCount").GetInt64() == 5 &&
                                 o.GetProperty("alertCount").GetInt64() == 4 &&
                                 o.GetProperty("onlineCount").GetInt64() == 1,
            $"站={o.GetProperty("stationCount").GetInt64()}, 文件={o.GetProperty("fileCount").GetInt64()}, 报警={o.GetProperty("alertCount").GetInt64()}");
    }
    catch (Exception ex)
    {
        Pass("负责人本部门及下级", false, ex.Message);
    }

    // ---------- 4. 采集趋势（近4天逐日） ----------
    try
    {
        using var admin = await Login("admin", "Admin@123");
        var points = (await GetAsync(admin, "/api/v1/stats/collection-trend?days=4")).GetProperty("data").EnumerateArray().ToList();
        var day3 = points[0];
        var day2 = points[1];
        var yesterday = points[2];
        var today = points[3];
        Pass("采集趋势逐日", today.GetProperty("fileCount").GetInt64() == 3 &&
                             today.GetProperty("size").GetInt64() == 7 * Mb &&
                             yesterday.GetProperty("fileCount").GetInt64() == 1 &&
                             yesterday.GetProperty("size").GetInt64() == 3 * Mb &&
                             day2.GetProperty("fileCount").GetInt64() == 0 &&
                             day3.GetProperty("fileCount").GetInt64() == 1 &&
                             day3.GetProperty("size").GetInt64() == 5 * Mb,
            $"今日={today.GetProperty("fileCount").GetInt64()}/{today.GetProperty("size").GetInt64()}, 昨日={yesterday.GetProperty("fileCount").GetInt64()}, 前2天={day2.GetProperty("fileCount").GetInt64()}, 前3天={day3.GetProperty("fileCount").GetInt64()}");

        var filtered = (await GetAsync(admin, $"/api/v1/stats/collection-trend?days=4&deptId={grp1Id}")).GetProperty("data").EnumerateArray().ToList();
        Pass("趋势按部门筛选", filtered[0].GetProperty("fileCount").GetInt64() == 1 &&
                              filtered[3].GetProperty("fileCount").GetInt64() == 1,
            $"grp1 今日={filtered[0].GetProperty("fileCount").GetInt64()}, 前3天={filtered[3].GetProperty("fileCount").GetInt64()}");
    }
    catch (Exception ex)
    {
        Pass("采集趋势", false, ex.Message);
    }

    // ---------- 5. 采集排行：按文件数倒序 ----------
    try
    {
        using var admin = await Login("admin", "Admin@123");
        var items = (await GetAsync(admin, "/api/v1/stats/stations?page=1&size=50")).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var first = items[0];
        var second = items[1];
        Pass("采集排行", items.Count == 2 &&
                         first.GetProperty("stationCode").GetString() == "ST-A" &&
                         first.GetProperty("fileCount").GetInt64() == 3 &&
                         first.GetProperty("totalSize").GetInt64() == 6 * Mb &&
                         first.GetProperty("alertCount").GetInt64() == 2 &&
                         first.GetProperty("isOnline").GetBoolean() &&
                         second.GetProperty("stationCode").GetString() == "ST-B" &&
                         second.GetProperty("fileCount").GetInt64() == 2 &&
                         !second.GetProperty("isOnline").GetBoolean() &&
                         second.GetProperty("deptName").GetString() == "一组",
            $"第一={first.GetProperty("stationCode").GetString()}({first.GetProperty("fileCount").GetInt64()}), 第二={second.GetProperty("stationCode").GetString()}({second.GetProperty("fileCount").GetInt64()})");
    }
    catch (Exception ex)
    {
        Pass("采集排行", false, ex.Message);
    }

    // ---------- 6. 报警统计 ----------
    try
    {
        using var admin = await Login("admin", "Admin@123");
        var a = (await GetAsync(admin, "/api/v1/stats/alerts")).GetProperty("data");
        var byLevel = a.GetProperty("byLevel").EnumerateArray().ToList();
        var byStatus = a.GetProperty("byStatus").EnumerateArray().ToList();
        var byType = a.GetProperty("byType").EnumerateArray().ToList();
        bool Has(List<JsonElement> list, string key, long count) =>
            list.Any(x => x.GetProperty("key").GetString() == key && x.GetProperty("count").GetInt64() == count);
        Pass("报警统计", byLevel.Count == 3 &&
                         Has(byLevel, "0", 1) && Has(byLevel, "1", 2) && Has(byLevel, "2", 1) &&
                         Has(byStatus, "0", 2) && Has(byStatus, "2", 1) && Has(byStatus, "3", 1) &&
                         byType.Count == 4,
            $"级别={string.Join(",", byLevel.Select(x => $"{x.GetProperty("key").GetString()}x{x.GetProperty("count").GetInt64()}"))}");
    }
    catch (Exception ex)
    {
        Pass("报警统计", false, ex.Message);
    }

    // ---------- 7. 心跳打点：轮询后 ST-B 在线 ----------
    try
    {
        using var admin = await Login("admin", "Admin@123");
        var before = (await GetAsync(admin, "/api/v1/stats/overview")).GetProperty("data").GetProperty("onlineCount").GetInt64();
        var poll = await admin.GetAsync($"/api/v1/stations/{stationBId}/commands/poll");
        var after = (await GetAsync(admin, "/api/v1/stats/overview")).GetProperty("data").GetProperty("onlineCount").GetInt64();
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var b = check.Queryable<PlatformStation>().First(s => s.Id == stationBId);
            Console.WriteLine($"[HEARTBEAT] poll={(int)poll.StatusCode}, ST-B LastHeartbeatAt={b.LastHeartbeatAt}, 距今分钟={(DateTime.Now - (b.LastHeartbeatAt ?? DateTime.Now)).TotalMinutes:F1}");
        }
        Pass("心跳打点在线率", poll.StatusCode == HttpStatusCode.OK && before == 1 && after == 2,
            $"poll={(int)poll.StatusCode}, 轮询前在线={before}, 轮询后在线={after}");
    }
    catch (Exception ex)
    {
        Pass("心跳打点在线率", false, ex.Message);
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

// ---------- 三库补列机制校验（MySQL 已随平台启动实测，此处验证 PG/Kingbase 通道） ----------
var dialectChecks = new[]
{
    (DbType.PostgreSQL, "Host=localhost;Port=5432;Database=station_spike;Username=station;Password=Station@123", "timestamp"),
    (DbType.Kdbndp, "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123", "timestamp")
};
foreach (var (dbType, conn, colType) in dialectChecks)
{
    try
    {
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = conn,
            DbType = dbType,
            IsAutoCloseConnection = true
        });
        db.Ado.ExecuteCommand("drop table if exists spike_m24_colcheck");
        db.Ado.ExecuteCommand("create table spike_m24_colcheck (Id bigint primary key)");
        db.Ado.ExecuteCommand($"alter table spike_m24_colcheck add column LastHeartbeatAt {colType} null");
        var has = db.DbMaintenance.GetColumnInfosByTableName("spike_m24_colcheck")
            .Any(c => string.Equals(c.DbColumnName, "LastHeartbeatAt", StringComparison.OrdinalIgnoreCase));
        db.Ado.ExecuteCommand("drop table spike_m24_colcheck");
        Pass($"{dbType} 补列机制", has, $"ALTER TABLE ADD COLUMN {colType} + DbMaintenance 读取生效");
    }
    catch (Exception ex)
    {
        Pass($"{dbType} 补列机制", false, ex.Message);
    }
}

Console.WriteLine();
Console.WriteLine("================ M24 跨站汇总统计 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
