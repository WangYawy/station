using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M30 Spike: 台账/报表 CSV 导出（权限 + 数据范围 + 脱敏 + BOM/转义）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5119;
const long Mb = 1024 * 1024;
long stationAId;

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
    seed.Ado.ExecuteCommand("delete from platform_alert_report");
    seed.Ado.ExecuteCommand("delete from platform_command");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A')");
    seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'recorder.%'");
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
        DeptId = team1.Id, RegisteredAt = DateTime.Now, LastHeartbeatAt = DateTime.Now
    }).ExecuteCommand();

    var now = DateTime.Now;
    seed.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationAId, DeptId = team1.Id, LocalAlertId = 1, Type = AlertType.UsbFault, Level = AlertLevel.Warning, Status = AlertStatus.Pending, Source = "ST-A", Message = "USB故障", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();
    seed.Insertable(new PlatformAlertReport { Id = id.NextId(), StationId = stationAId, DeptId = team1.Id, LocalAlertId = 2, Type = AlertType.NetworkDown, Level = AlertLevel.Critical, Status = AlertStatus.Pending, Source = "ST-A", Message = "网络中断", OccurredAt = now, ReceivedAt = now }).ExecuteCommand();
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

async Task<(HttpStatusCode Status, byte[] Body, string Text, string? Cd)> Export(HttpClient http, string url)
{
    var resp = await http.GetAsync(url);
    var bytes = resp.StatusCode == HttpStatusCode.OK ? await resp.Content.ReadAsByteArrayAsync() : [];
    var text = resp.StatusCode == HttpStatusCode.OK ? Encoding.UTF8.GetString(bytes) : string.Empty;
    var cd = resp.Content.Headers.ContentDisposition?.ToString();
    return (resp.StatusCode, bytes, text, cd);
}

try
{
    // 上报产生文件与记录仪台账（含逗号文件名验证转义）
    using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") })
    {
        var now = DateTime.Now;
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 1L, fileNo = "ST-A-1", fileName = "a.mp4", size = 2 * Mb, kind = 0, sm3 = "s1", collectedAt = now, recorderSerial = "R-001" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 2L, fileNo = "ST-A-2", fileName = "b.mp4", size = 3 * Mb, kind = 0, sm3 = "s2", collectedAt = now, recorderSerial = "R-001" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 3L, fileNo = "ST-A-3", fileName = "c.wav", size = 4 * Mb, kind = 1, sm3 = "s3", collectedAt = now, recorderSerial = "R-002" });
        await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", new { stationId = stationAId, localFileId = 4L, fileNo = "ST-A-4", fileName = "a,b.mp4", size = 1 * Mb, kind = 0, sm3 = "s4", collectedAt = now, recorderSerial = "R-003" });
    }

    // 白名单 + 绑定产生审计日志（来源 IP 将脱敏）
    long recorderId;
    using (var seedAdmin = await Login(PlatformPort, "admin", "Admin@123"))
    {
        var page = await (await seedAdmin.GetAsync("/api/v1/recorders?page=1&size=50")).Content.ReadFromJsonAsync<RecorderPage>();
        recorderId = page!.Data!.Items!.First(x => x.RecorderSerial == "R-001").Id;
        await seedAdmin.PutAsJsonAsync($"/api/v1/recorders/{recorderId}/whitelist", new { isWhitelisted = true });
        await seedAdmin.PutAsJsonAsync($"/api/v1/recorders/{recorderId}/bind", new { userNo = "zhangsan" });
    }

    // ---------- 0. 未登录导出 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync("/api/v1/exports/files");
        Pass("未登录导出401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录导出401", false, ex.Message);
    }

    using var admin = await Login(PlatformPort, "admin", "Admin@123");

    // ---------- 1. 文件导出：BOM + 中文表头 + 逗号文件名转义 ----------
    try
    {
        var (status, bytes, text, cd) = await Export(admin, "/api/v1/exports/files");
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var hasHeader = text.Contains("编号") && text.Contains("文件名");
        var hasEscaped = text.Contains("\"a,b.mp4\"");
        Pass("文件导出", status == HttpStatusCode.OK &&
                         hasBom && hasHeader && text.Contains("ST-A-1") &&
                         text.Contains("视频") && hasEscaped &&
                         cd is not null && cd.Contains(".csv"),
            $"BOM={(hasBom ? "有" : "无")}, 含表头={hasHeader}, 逗号转义={hasEscaped}");
    }
    catch (Exception ex)
    {
        Pass("文件导出", false, ex.Message);
    }

    // ---------- 2. 报警导出 ----------
    try
    {
        var (status, _, text, _) = await Export(admin, "/api/v1/exports/alerts");
        Pass("报警导出", status == HttpStatusCode.OK && text.Contains("级别") && text.Contains("严重") && text.Contains("网络中断"),
            $"HTTP {(int)status}");
    }
    catch (Exception ex)
    {
        Pass("报警导出", false, ex.Message);
    }

    // ---------- 3. 记录仪导出 ----------
    try
    {
        var (status, _, text, _) = await Export(admin, "/api/v1/exports/recorders");
        Pass("记录仪导出", status == HttpStatusCode.OK && text.Contains("记录仪编号") && text.Contains("R-001") && text.Contains("是"),
            $"HTTP {(int)status}");
    }
    catch (Exception ex)
    {
        Pass("记录仪导出", false, ex.Message);
    }

    // ---------- 4. 统计导出（趋势 + 排行） ----------
    try
    {
        var (tStatus, _, tText, _) = await Export(admin, "/api/v1/exports/stats-trend?days=4");
        var (sStatus, _, sText, _) = await Export(admin, "/api/v1/exports/stats-stations");
        Pass("统计导出", tStatus == HttpStatusCode.OK && tText.Contains("日期") && tText.Contains("文件数") &&
                        sStatus == HttpStatusCode.OK && sText.Contains("ST-A") && sText.Contains("在线"),
            $"趋势HTTP {(int)tStatus}, 排行HTTP {(int)sStatus}");
    }
    catch (Exception ex)
    {
        Pass("统计导出", false, ex.Message);
    }

    // ---------- 5. 审计导出：权限 + 来源IP脱敏 ----------
    try
    {
        var (status, _, text, _) = await Export(admin, "/api/v1/exports/audit-logs");
        Pass("审计导出脱敏", status == HttpStatusCode.OK && text.Contains("来源IP(脱敏)") &&
                            text.Contains("127.0.0.*") && !text.Contains("127.0.0.1"),
            $"HTTP {(int)status}, 掩码出现={text.Contains("127.0.0.*")}, 原始IP出现={text.Contains("127.0.0.1")}");
    }
    catch (Exception ex)
    {
        Pass("审计导出脱敏", false, ex.Message);
    }

    // ---------- 6. 权限与数据范围：操作员仅本组文件、无审计导出权限 ----------
    try
    {
        using var op = await Login(PlatformPort, "zhangsan", "Test@123");
        var (fStatus, _, fText, _) = await Export(op, "/api/v1/exports/files");
        var (aStatus, _, _, _) = await Export(op, "/api/v1/exports/audit-logs");
        Pass("导出权限与范围", fStatus == HttpStatusCode.OK && fText.Contains("编号") && !fText.Contains("ST-A-1") &&
                              aStatus == HttpStatusCode.Forbidden,
            $"文件导出{(int)fStatus}(仅表头={!fText.Contains("ST-A-1")}), 审计导出={(int)aStatus}");
    }
    catch (Exception ex)
    {
        Pass("导出权限与范围", false, ex.Message);
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
    clean.Ado.ExecuteCommand("delete from platform_alert_report");
    clean.Ado.ExecuteCommand("delete from platform_command");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-A'");
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'recorder.%'");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M30 台账/报表 CSV 导出 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record RecorderPage(bool Success, int Code, string Message, RecorderPageData? Data);

internal sealed record RecorderPageData(int PageIndex, int PageSize, long TotalCount, List<RecorderItemJson>? Items);

internal sealed record RecorderItemJson(long Id, string RecorderSerial);
