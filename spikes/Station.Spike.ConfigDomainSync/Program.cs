using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Authentication;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Sync;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

// ---------------------------------------------------------------------------
// M46 Spike: 用户域配置全量下发（组织/用户/账号/角色/记录仪白名单）
// 平台发布快照 → 采集站轮询拉取 → 本地自然键 upsert → 断网本地登录可用
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
const int PlatformPort = 5140;

var testRoot = @"E:\Reny\station\archive\config-domain-test";
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

// ---------- 0. 平台库播种（组织/用户/账号/角色/记录仪台账） ----------
long stationAId = 0;
using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from platform_config_change where StationId in (select Id from platform_station where StationCode='ST-DOM')");
    seed.Ado.ExecuteCommand("delete from platform_recorder where RecorderSerial in ('R-DOM-001','R-DOM-002')");
    seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi'))");
    seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi')");
    seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi')");
    seed.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1')");
    seed.Ado.ExecuteCommand("delete from platform_station where StationCode='ST-DOM'");

    var id = new SnowflakeIdGenerator();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1, IsActive = true };
    var grp1 = new Dept { Id = id.NextId(), Code = "GRP1", Name = "一组", ParentId = team1.Id, SortOrder = 1, IsActive = true };
    seed.Insertable(team1).ExecuteCommand();
    seed.Insertable(grp1).ExecuteCommand();

    var zhangSan = new User { Id = id.NextId(), UserNo = "zhangsan", Name = "张三", DeptId = grp1.Id, IsActive = true };
    var liSi = new User { Id = id.NextId(), UserNo = "lisi", Name = "李四", DeptId = team1.Id, IsActive = true };
    seed.Insertable(zhangSan).ExecuteCommand();
    seed.Insertable(liSi).ExecuteCommand();

    var operatorRole = seed.Queryable<Role>().Where(r => r.Code == "operator").First();
    var managerRole = seed.Queryable<Role>().Where(r => r.Code == "manager").First();
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRole.Id }).ExecuteCommand();
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = liSi.Id, RoleId = managerRole.Id }).ExecuteCommand();

    var hasher = new Sm3PasswordHasher();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id, IsEnabled = true }).ExecuteCommand();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "lisi", PasswordHash = hasher.Hash("Test@123"), UserId = liSi.Id, IsEnabled = true }).ExecuteCommand();

    var now = DateTime.Now;
    seed.Insertable(new PlatformRecorder
    {
        Id = id.NextId(),
        RecorderSerial = "R-DOM-001",
        BoundUserNo = "zhangsan",
        BoundDeptCode = "GRP1",
        IsWhitelisted = true,
        IsActive = true,
        FirstSeenAt = now,
        LastSeenAt = now,
        UpdatedAt = now
    }).ExecuteCommand();
    seed.Insertable(new PlatformRecorder
    {
        Id = id.NextId(),
        RecorderSerial = "R-DOM-002",
        BoundUserNo = "lisi",
        BoundDeptCode = "TEAM1",
        IsWhitelisted = false,
        IsActive = true,
        FirstSeenAt = now,
        LastSeenAt = now,
        UpdatedAt = now
    }).ExecuteCommand();
}

// ---------- 1. 启动平台 ----------
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

    // ---------- 2. 启动采集站本地宿主（临时 SQLite + 平台模式） ----------
    Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
    Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
    Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
    Environment.SetEnvironmentVariable("STATION__WEB__PORT", "5299");
    Environment.SetEnvironmentVariable("STATION__PLATFORM__ENABLED", "true");
    Environment.SetEnvironmentVariable("STATION__PLATFORM__BASEURL", $"http://127.0.0.1:{PlatformPort}");
    Environment.SetEnvironmentVariable("STATION__PLATFORM__STATIONCODE", "ST-DOM");
    Environment.SetEnvironmentVariable("STATION__PLATFORM__SYNCINTERVALSECONDS", "3");
    Environment.SetEnvironmentVariable("STATION__PLATFORM__COMMANDPOLLINTERVALSECONDS", "3");
    Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");

    var host = HostBuilderFactory.Create().Build();
    try
    {
        await host.StartAsync();

        var context = host.Services.GetRequiredService<IStationContext>();
        var registerDeadline = DateTime.Now.AddSeconds(30);
        while (context.StationId is null && DateTime.Now < registerDeadline)
        {
            await Task.Delay(300);
        }

        if (context.StationId is not { } stationId)
        {
            Pass("采集站注册", false, "30 秒内未完成注册");
            return 1;
        }

        stationAId = stationId;
        Pass("采集站注册", true, $"StationId={stationId}");

        using var admin = await LoginPlatformAsync("admin", "Admin@123");

        // ---------- 3. 未授权发布被拒 ----------
        try
        {
            using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
            var resp = await anon.PostAsync($"/api/v1/stations/{stationId}/configs/sync-domain", null);
            Pass("未授权发布被拒", resp.StatusCode == HttpStatusCode.Unauthorized,
                $"匿名调用={resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Pass("未授权发布被拒", false, ex.Message);
        }

        // ---------- 4. 发布用户域快照 ----------
        List<PublishedConfigItem>? published = null;
        try
        {
            var resp = await admin.PostAsync($"/api/v1/stations/{stationId}/configs/sync-domain", null);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<DomainPublishResponse>();
            published = body?.Data;
            var types = published?.Select(p => p.EntityType).ToList() ?? [];
            Pass("发布用户域快照", published?.Count == 6 &&
                                 types.SequenceEqual(new[]
                                 {
                                     "Dept", "User", "Role", "UserRole", "Account", "Recorder"
                                 }),
                $"发布={published?.Count} 项：{string.Join(",", types)}");
        }
        catch (Exception ex)
        {
            Pass("发布用户域快照", false, ex.ToString());
        }

        // ---------- 5. 采集站轮询拉取并应用 ----------
        try
        {
            var state = host.Services.GetRequiredService<IConfigSyncState>();
            var applyDeadline = DateTime.Now.AddSeconds(40);
            while (state.AppliedVersions.Count < 6 && DateTime.Now < applyDeadline)
            {
                await Task.Delay(500);
            }

            var hasAll = ConfigDomainPayload.PublishOrder.All(t => state.AppliedVersions.ContainsKey(t));
            Pass("轮询拉取应用", hasAll,
                $"已应用={string.Join(",", state.AppliedVersions.Select(kv => $"{kv.Key}@{kv.Value}"))}");
        }
        catch (Exception ex)
        {
            Pass("轮询拉取应用", false, ex.Message);
        }

        // ---------- 6. 本地库断言（自然键 upsert + 外键重建） ----------
        try
        {
            var db = host.Services.GetRequiredService<ISqlSugarClient>();
            var depts = await db.Queryable<Dept>().ToListAsync();
            var users = await db.Queryable<User>().ToListAsync();
            var accounts = await db.Queryable<Account>().ToListAsync();
            var roles = await db.Queryable<Role>().ToListAsync();
            var links = await db.Queryable<UserRole>().ToListAsync();
            var recorders = await db.Queryable<Recorder>().ToListAsync();

            var team1 = depts.FirstOrDefault(d => d.Code == "TEAM1");
            var grp1 = depts.FirstOrDefault(d => d.Code == "GRP1");
            var root = depts.FirstOrDefault(d => d.Code == "ROOT");
            var zhangsan = users.FirstOrDefault(u => u.UserNo == "zhangsan");
            var lisi = users.FirstOrDefault(u => u.UserNo == "lisi");
            var zsAccount = accounts.FirstOrDefault(a => a.UserName == "zhangsan");
            var operatorRole = roles.FirstOrDefault(r => r.Code == "operator");
            var managerRole = roles.FirstOrDefault(r => r.Code == "manager");
            var r1 = recorders.FirstOrDefault(r => r.SerialNumber == "R-DOM-001");
            var r2 = recorders.FirstOrDefault(r => r.SerialNumber == "R-DOM-002");

            var deptOk = team1 is { IsActive: true } &&
                         grp1 is { IsActive: true } &&
                         team1.ParentId == root?.Id &&
                         grp1.ParentId == team1.Id;
            var userOk = zhangsan is { IsActive: true } && zhangsan.DeptId == grp1?.Id &&
                         lisi is { IsActive: true } && lisi.DeptId == team1?.Id;
            var accountOk = zsAccount is { IsEnabled: true } && zsAccount.UserId == zhangsan?.Id &&
                            new Sm3PasswordHasher().Verify("Test@123", zsAccount!.PasswordHash);
            var roleOk = operatorRole is { IsActive: true } && managerRole is { IsActive: true } &&
                         links.Any(l => l.UserId == zhangsan?.Id && l.RoleId == operatorRole?.Id) &&
                         links.Any(l => l.UserId == lisi?.Id && l.RoleId == managerRole?.Id);
            var recorderOk = r1 is { IsAuthorized: true, IsActive: true } && r1.BoundUserId == zhangsan?.Id &&
                             r1.DeptId == grp1?.Id &&
                             r2 is { IsAuthorized: false, IsActive: true };

            var rolePermCount = await db.Queryable<RolePermission>().CountAsync();
            Pass("本地库快照断言", deptOk && userOk && accountOk && roleOk && recorderOk && rolePermCount > 0,
                $"部门={depts.Count}, 用户={users.Count}, 账号={accounts.Count}, 角色={roles.Count}, 用户角色={links.Count}, 权限关联={rolePermCount}, 记录仪={recorders.Count} | " +
                $"dept={deptOk}, user={userOk}, account={accountOk}, role={roleOk}, recorder={recorderOk}");
        }
        catch (Exception ex)
        {
            Pass("本地库快照断言", false, ex.ToString());
        }

        // ---------- 7. 断网本地登录（平台账号已落本地库） ----------
        try
        {
            var auth = host.Services.GetRequiredService<IAuthenticationService>();
            var result = await auth.LoginAsync(new LoginRequest("zhangsan", "Test@123"));
            var roles = result.Session?.Roles ?? [];
            Pass("断网本地登录", result.Success && roles.Contains("operator"),
                $"登录={result.Success}, 角色={string.Join(",", roles)}");
        }
        catch (Exception ex)
        {
            Pass("断网本地登录", false, ex.Message);
        }

        // ---------- 8. 二次发布：白名单调整后增量同步 ----------
        try
        {
            using var updateSeed = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = platformMysql,
                DbType = DbType.MySql,
                IsAutoCloseConnection = true
            });
            var r2Platform = updateSeed.Queryable<PlatformRecorder>()
                .Where(r => r.RecorderSerial == "R-DOM-002").First();
            r2Platform.IsWhitelisted = true;
            r2Platform.UpdatedAt = DateTime.Now;
            updateSeed.Updateable(r2Platform).ExecuteCommand();

            var state = host.Services.GetRequiredService<IConfigSyncState>();
            var before = state.AppliedVersions.TryGetValue(ConfigDomainPayload.EntityTypeRecorder, out var v) ? v : 0;
            var resp = await admin.PostAsync($"/api/v1/stations/{stationId}/configs/sync-domain", null);
            resp.EnsureSuccessStatusCode();

            var applyDeadline = DateTime.Now.AddSeconds(40);
            long after = before;
            while (DateTime.Now < applyDeadline)
            {
                after = state.AppliedVersions.TryGetValue(ConfigDomainPayload.EntityTypeRecorder, out var nv) ? nv : 0;
                if (after > before)
                {
                    break;
                }

                await Task.Delay(500);
            }

            var db = host.Services.GetRequiredService<ISqlSugarClient>();
            var r2Local = await db.Queryable<Recorder>()
                .Where(r => r.SerialNumber == "R-DOM-002").FirstAsync();
            Pass("白名单增量同步", after > before && r2Local is { IsAuthorized: true },
                $"版本 {before}→{after}, 本地 R-DOM-002 白名单={r2Local?.IsAuthorized}");
        }
        catch (Exception ex)
        {
            Pass("白名单增量同步", false, ex.ToString());
        }

        // ---------- 9. 平台审计留痕 ----------
        try
        {
            var logs = await (await admin.GetAsync("/api/v1/audit-logs?page=1&size=20"))
                .Content.ReadFromJsonAsync<AuditPage>();
            var publishedLogs = logs?.Data?.Items?.Count(x => x.OperationType == "config.publish-domain") ?? 0;
            Pass("平台审计留痕", publishedLogs >= 2, $"config.publish-domain 审计={publishedLogs} 条");
        }
        catch (Exception ex)
        {
            Pass("平台审计留痕", false, ex.Message);
        }
    }
    finally
    {
        await host.StopAsync();
        host.Dispose();
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine("================ 用户域配置同步验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

static async Task<HttpClient> LoginPlatformAsync(string user, string pass)
{
    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password = pass });
    login.EnsureSuccessStatusCode();
    return http;
}

internal sealed record DomainPublishResponse(bool Success, int Code, string Message, List<PublishedConfigItem>? Data);

internal sealed record PublishedConfigItem(string EntityType, long Version, int Rows);

internal sealed record AuditPage(bool Success, int Code, string Message, AuditData? Data);

internal sealed record AuditData(int Total, List<AuditItem>? Items);

internal sealed record AuditItem(string OperationType, string? Target, string? Detail);
