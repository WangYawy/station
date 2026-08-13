using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M29 Spike: 平台记录仪台账（归集/白名单/绑定 + WriteBinding 指令端到端）
//  - 文件元数据上报自动归集台账（重复上报不累计）
//  - 白名单/绑定 API + 数据范围；绑定后向最近采集站下发 WriteBinding 指令
//  - 桌面端执行 WriteBinding：更新本地台账，接入时自动写 ini
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5118;
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
    seed.Ado.ExecuteCommand("delete from platform_command");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST-BIND')");
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
        DeptId = team1.Id, RegisteredAt = DateTime.Now
    }).ExecuteCommand();
}

var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platformLog = Path.Combine(Path.GetTempPath(), $"station-m29-platform-{Guid.NewGuid():N}.log");
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = platformMysql
    }
});
platform.OutputDataReceived += (_, e) =>
{
    if (e.Data is not null)
    {
        File.AppendAllText(platformLog, e.Data + Environment.NewLine);
    }
};
platform.ErrorDataReceived += (_, e) =>
{
    if (e.Data is not null)
    {
        File.AppendAllText(platformLog, e.Data + Environment.NewLine);
    }
};
platform.BeginOutputReadLine();
platform.BeginErrorReadLine();

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

long recorderR001Id = 0;

try
{
    // ---------- 0. 未登录访问台账 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync("/api/v1/recorders?page=1&size=10");
        Pass("未登录台账401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录台账401", false, ex.Message);
    }

    // ---------- 1. 元数据上报自动归集（含重复上报去重） ----------
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var now = DateTime.Now;
        var report1 = new
        {
            stationId = stationAId,
            localFileId = 1L,
            fileNo = "ST-A-1",
            fileName = "a.mp4",
            size = 2 * Mb,
            kind = 0,
            sm3 = "s1",
            collectedAt = now,
            recorderSerial = "R-001"
        };
        var report2 = new
        {
            stationId = stationAId,
            localFileId = 2L,
            fileNo = "ST-A-2",
            fileName = "b.mp4",
            size = 3 * Mb,
            kind = 0,
            sm3 = "s2",
            collectedAt = now,
            recorderSerial = "R-001"
        };
        var report3 = new
        {
            stationId = stationAId,
            localFileId = 3L,
            fileNo = "ST-A-3",
            fileName = "c.wav",
            size = 4 * Mb,
            kind = 1,
            sm3 = "s3",
            collectedAt = now,
            recorderSerial = "R-002"
        };
        var r1Resp = await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", report1);
        var r2Resp = await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", report2);
        var r3Resp = await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", report3);
        var r1dupResp = await http.PostAsJsonAsync($"/api/v1/stations/{stationAId}/files/metadata", report1); // 重复上报
        Console.WriteLine($"[DIAG] 上报状态: r1={(int)r1Resp.StatusCode}, r2={(int)r2Resp.StatusCode}, r3={(int)r3Resp.StatusCode}, r1dup={(int)r1dupResp.StatusCode}");
        foreach (var resp in new[] { r1Resp, r2Resp, r3Resp, r1dupResp })
        {
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[DIAG] 失败响应: {await resp.Content.ReadAsStringAsync()}");
            }
        }

        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        using (var direct = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            Console.WriteLine($"[DIAG] 直连DB记录仪数={direct.Queryable<PlatformRecorder>().Count()}");
        }

        var page = await GetJson(admin, "/api/v1/recorders?page=1&size=50");
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var r1 = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-001");
        var r2 = items.First(x => x.GetProperty("recorderSerial").GetString() == "R-002");
        recorderR001Id = r1.GetProperty("id").GetInt64();
        Pass("台账归集与去重", items.Count == 2 &&
                             r1.GetProperty("fileCount").GetInt64() == 2 &&
                             r1.GetProperty("totalSize").GetInt64() == 5 * Mb &&
                             r2.GetProperty("fileCount").GetInt64() == 1 &&
                             r2.GetProperty("totalSize").GetInt64() == 4 * Mb &&
                             r1.GetProperty("lastStationId").GetInt64() == stationAId,
            $"R-001={r1.GetProperty("fileCount").GetInt64()}文件/{r1.GetProperty("totalSize").GetInt64()}字节, R-002={r2.GetProperty("fileCount").GetInt64()}文件");
    }
    catch (Exception ex)
    {
        Pass("台账归集与去重", false, ex.Message);
    }

    // ---------- 2. 白名单 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var resp = await admin.PutAsJsonAsync($"/api/v1/recorders/{recorderR001Id}/whitelist", new { isWhitelisted = true });
        var page = await GetJson(admin, $"/api/v1/recorders?page=1&size=50&whitelisted=true");
        var r1 = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("recorderSerial").GetString() == "R-001");
        Pass("白名单", resp.StatusCode == HttpStatusCode.OK && r1.GetProperty("isWhitelisted").GetBoolean(),
            $"HTTP {(int)resp.StatusCode}, isWhitelisted={r1.GetProperty("isWhitelisted").GetBoolean()}");
    }
    catch (Exception ex)
    {
        Pass("白名单", false, ex.Message);
    }

    // ---------- 3. 绑定：更新台账 + 下发 WriteBinding 指令 ----------
    try
    {
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var resp = await admin.PutAsJsonAsync($"/api/v1/recorders/{recorderR001Id}/bind", new { userNo = "zhangsan" });
        Console.WriteLine($"[DIAG] bind 响应: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<BindResponse>();
        long commandId = 0;
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var command = check.Queryable<PlatformCommand>()
                .Where(c => c.Type == CommandType.WriteBinding && c.StationId == stationAId)
                .First();
            commandId = command.Id;
            Console.WriteLine($"[DIAG] 指令 PayloadJson={command.PayloadJson}");
            var payload = JsonDocument.Parse(command.PayloadJson!).RootElement;
            Pass("绑定下发指令", resp.StatusCode == HttpStatusCode.OK &&
                                body!.Data!.Dispatched &&
                                command.Type == CommandType.WriteBinding &&
                                payload.GetProperty("recorderSerial").GetString() == "R-001" &&
                                payload.GetProperty("userNo").GetString() == "zhangsan",
                $"commandId={commandId}, payload serial={payload.GetProperty("recorderSerial").GetString()}");
        }

        var page = await GetJson(admin, "/api/v1/recorders?page=1&size=50&bound=true");
        var r1 = page.GetProperty("data").GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("recorderSerial").GetString() == "R-001");
        Pass("台账绑定字段", r1.GetProperty("boundUserNo").GetString() == "zhangsan" &&
                           r1.GetProperty("boundDeptCode").GetString() == "GRP1",
            $"user={r1.GetProperty("boundUserNo").GetString()}, dept={r1.GetProperty("boundDeptCode").GetString()}");
    }
    catch (Exception ex)
    {
        Pass("绑定下发指令", false, ex.Message);
    }

    // ---------- 4. 数据范围：负责人全量可见；操作员无权限 ----------
    try
    {
        using var manager = await Login(PlatformPort, "lisi", "Test@123");
        var page = await GetJson(manager, "/api/v1/recorders?page=1&size=50");
        var count = page.GetProperty("data").GetProperty("totalCount").GetInt64();
        using var op = await Login(PlatformPort, "zhangsan", "Test@123");
        var denied = await op.GetAsync("/api/v1/recorders?page=1&size=10");
        Pass("台账数据范围", count == 2 && denied.StatusCode == HttpStatusCode.Forbidden,
            $"负责人见 {count} 台, 操作员={(int)denied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("台账数据范围", false, ex.Message);
    }

    // ================= Phase B：桌面端执行 WriteBinding 指令 =================
    try
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "station-m29-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var dbFile = Path.Combine(testRoot, "desktop.db");
        Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
        Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
        Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
        Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
        Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
        Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", $"http://127.0.0.1:{PlatformPort}");
        Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-BIND");
        Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

        using var host = HostBuilderFactory.Create().Build();
        await host.StartAsync();
        var context = host.Services.GetRequiredService<IStationContext>();
        var registerDeadline = DateTime.Now.AddSeconds(30);
        while (context.StationId is null && DateTime.Now < registerDeadline)
        {
            await Task.Delay(300);
        }

        var bindStationId = context.StationId!.Value;
        using var admin = await Login(PlatformPort, "admin", "Admin@123");
        var payload = JsonSerializer.Serialize(new
        {
            recorderSerial = "R-BIND",
            model = "M-X",
            userNo = "admin",
            deptId = 1,
            boundAt = DateTime.Now
        });
        var dispatch = await admin.PostAsJsonAsync(
            $"/api/v1/stations/{bindStationId}/commands",
            new { type = (int)CommandType.WriteBinding, payloadJson = payload, timeoutSeconds = 300 });
        var dispatchBody = await dispatch.Content.ReadFromJsonAsync<DispatchResponse>();
        var bindCommandId = dispatchBody!.Data!.CommandId;

        var commands = host.Services.GetRequiredService<ICommandService>();
        var outbox = host.Services.GetRequiredService<ISyncOutboxService>();
        await commands.PollAndExecuteAsync(bindStationId);
        await outbox.DrainAsync();

        var db = host.Services.GetRequiredService<ISqlSugarClient>();
        var localRecorder = db.Queryable<Recorder>().Where(r => r.SerialNumber == "R-BIND").First();
        var list = await admin.GetFromJsonAsync<CommandListResponse>(
            $"/api/v1/stations/{bindStationId}/commands");
        var executed = list!.Data!.First(c => c.CommandId == bindCommandId);
        Pass("桌面端执行WriteBinding", dispatch.StatusCode == HttpStatusCode.OK &&
                                      executed.Status == CommandStatus.Succeeded &&
                                      executed.Message!.Contains("绑定已更新") &&
                                      localRecorder is not null &&
                                      localRecorder.BoundUserId == 1 &&
                                      localRecorder.IsAuthorized,
            $"指令状态={executed.Status}, 本地台账 serial={localRecorder?.SerialNumber} user={localRecorder?.BoundUserId} 白名单={localRecorder?.IsAuthorized}");

        await host.StopAsync();
        try
        {
            Directory.Delete(testRoot, true);
        }
        catch
        {
        }
    }
    catch (Exception ex)
    {
        Pass("桌面端执行WriteBinding", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        try
        {
            platform.Kill();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAG] Kill 平台进程失败: {ex.Message}");
        }

        platform.WaitForExit(5000);
    }

    try
    {
        if (File.Exists(platformLog))
        {
            var tail = string.Join(Environment.NewLine, File.ReadLines(platformLog).TakeLast(20));
            Console.WriteLine($"[DIAG] 平台日志尾部:{Environment.NewLine}{tail}");
        }
    }
    catch
    {
    }
}

using (var clean = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    var recorderDeleted = clean.Ado.ExecuteCommand("delete from platform_recorder");
    var fileDeleted = clean.Ado.ExecuteCommand("delete from platform_file_metadata");
    Console.WriteLine($"[DIAG] 清理: recorders={recorderDeleted}, files={fileDeleted}");
    clean.Ado.ExecuteCommand("delete from platform_command");
    clean.Ado.ExecuteCommand("delete from platform_station where StationCode in ('ST-A','ST-BIND')");
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'recorder.%'");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
}

Console.WriteLine();
Console.WriteLine("================ M29 平台记录仪台账 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record BindResponse(bool Success, int Code, string Message, BindData? Data);

internal sealed record BindData(bool Dispatched, long? CommandId);

internal sealed record DispatchResponse(bool Success, int Code, string Message, DispatchData? Data);

internal sealed record DispatchData(long CommandId, int Status, string Message);

internal sealed record CommandListResponse(bool Success, int Code, string Message, List<CommandView>? Data);

internal sealed record CommandView(long CommandId, int Type, CommandStatus Status, DateTime IssuedAt, DateTime? FinishedAt, string? Message);
