using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Settings;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M49 Spike: 系统设置（单机 Web + 桌面共享服务）
// 验证：设置读取/分组修改/热应用/运行时文件持久化/授权激活/自检/报告/权限
// ---------------------------------------------------------------------------

const int WebPort = 5125;
Environment.CurrentDirectory = @"E:\Reny\station\src\Station.Desktop\Station.Desktop.WebHost";
var testRoot = Path.Combine(Path.GetTempPath(), "station-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var dbFile = Path.Combine(testRoot, "station.db");
var runtimeFile = Path.Combine(testRoot, "appsettings.runtime.json");

Environment.SetEnvironmentVariable("STATION__WEB__PORT", WebPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");
Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(testRoot, "cache"));
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", Path.Combine(testRoot, "sim"));
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");
Environment.SetEnvironmentVariable("STATION__RUNTIMESETTINGSPATH", runtimeFile);

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

using var host = HostBuilderFactory.Create().Build();
await host.StartAsync();

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

// 播种：部门负责人（setting:view）、操作员（无 setting 权限）账号
using (var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = $"Data Source={dbFile}",
    DbType = DbType.Sqlite,
    IsAutoCloseConnection = true
}))
{
    var id = new SnowflakeIdGenerator();
    var hasher = new Sm3PasswordHasher();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
    db.Insertable(team1).ExecuteCommand();
    var manager = new User { Id = id.NextId(), UserNo = "mgr", Name = "部门负责人", DeptId = team1.Id };
    var operatorUser = new User { Id = id.NextId(), UserNo = "op", Name = "操作员", DeptId = team1.Id };
    db.Insertable(manager).ExecuteCommand();
    db.Insertable(operatorUser).ExecuteCommand();
    var managerRole = db.Queryable<Role>().Where(r => r.Code == "manager").First();
    var operatorRole = db.Queryable<Role>().Where(r => r.Code == "operator").First();
    db.Insertable(new UserRole { Id = id.NextId(), UserId = manager.Id, RoleId = managerRole.Id }).ExecuteCommand();
    db.Insertable(new UserRole { Id = id.NextId(), UserId = operatorUser.Id, RoleId = operatorRole.Id }).ExecuteCommand();
    db.Insertable(new Account { Id = id.NextId(), UserName = "mgr", PasswordHash = hasher.Hash("Test@123"), UserId = manager.Id }).ExecuteCommand();
    db.Insertable(new Account { Id = id.NextId(), UserName = "op", PasswordHash = hasher.Hash("Test@123"), UserId = operatorUser.Id }).ExecuteCommand();
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
    // ---------- 1. 管理员读取设置 ----------
    using var admin = await Login(WebPort, "admin", "Admin@123");
    var settings = (await admin.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
    Pass("设置读取（分组齐全）",
        settings?.Basic is not null && settings.Storage is not null && settings.Collect is not null &&
        settings.Network is not null && settings.License is not null,
        $"basic.RunMode={settings?.Basic?.RunMode}, storage.Target={settings?.Storage?.Target}, license={settings?.License?.Message}");

    // ---------- 2. 采集策略修改：热应用 + 运行时文件 ----------
    var collectResp = await admin.PutAsJsonAsync("/api/v1/settings/collect", new
    {
        group = "collect",
        values = new Dictionary<string, string>
        {
            ["autoCollectOnConnect"] = "false",
            ["collectAncillaryFiles"] = "false"
        }
    });
    var collectBody = await collectResp.Content.ReadFromJsonAsync<Resp<HintsResp>>();
    Pass("采集策略修改", collectResp.IsSuccessStatusCode && collectBody?.Success == true,
        collectBody?.Message ?? $"{(int)collectResp.StatusCode}");

    using (var scope = host.Services.CreateScope())
    {
        var service = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        var core = await service.GetCoreAsync();
        Pass("采集策略热应用", core.Collect.AutoCollectOnConnect == false &&
                             core.Collect.CollectAncillaryFiles == false,
            $"auto={core.Collect.AutoCollectOnConnect}, ancillary={core.Collect.CollectAncillaryFiles}");
    }

    // ---------- 3. 存储策略修改（提示重启） ----------
    var storageResp = await admin.PutAsJsonAsync("/api/v1/settings/storage", new
    {
        group = "storage",
        values = new Dictionary<string, string>
        {
            ["videoRetentionDays"] = "120",
            ["cleanupTime"] = "03:30"
        }
    });
    var storageBody = await storageResp.Content.ReadFromJsonAsync<Resp<HintsResp>>();
    Pass("存储策略修改（重启提示）", storageResp.IsSuccessStatusCode &&
                                   storageBody?.Data?.Hints?.Any(h => h.Contains("重启")) == true,
        string.Join("；", storageBody?.Data?.Hints ?? []));

    // ---------- 4. 网络安全修改（重启提示 + 登录锁定热应用） ----------
    var networkResp = await admin.PutAsJsonAsync("/api/v1/settings/network", new
    {
        group = "network",
        values = new Dictionary<string, string>
        {
            ["webPort"] = "5126",
            ["maxFailedAttempts"] = "3",
            ["lockoutMinutes"] = "10"
        }
    });
    var networkBody = await networkResp.Content.ReadFromJsonAsync<Resp<HintsResp>>();
    var networkAfter = (await admin.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
    Pass("网络安全修改", networkResp.IsSuccessStatusCode && networkAfter?.Network?.MaxFailedAttempts == 3,
        $"maxFailed={networkAfter?.Network?.MaxFailedAttempts}, hints={string.Join("；", networkBody?.Data?.Hints ?? [])}");

    // ---------- 5. 基本设置：切平台版 → 只读 ----------
    var basicResp = await admin.PutAsJsonAsync("/api/v1/settings/basic", new
    {
        group = "basic",
        values = new Dictionary<string, string>
        {
            ["installLocation"] = "一楼大厅东侧",
            ["runMode"] = "platform",
            ["platformBaseUrl"] = "http://127.0.0.1:5160"
        }
    });
    var afterPlatform = (await admin.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
    Pass("基本设置切换平台版", basicResp.IsSuccessStatusCode &&
                             afterPlatform?.Basic?.RunMode == "platform" &&
                             afterPlatform?.ReadOnly == true,
        $"runMode={afterPlatform?.Basic?.RunMode}, readOnly={afterPlatform?.ReadOnly}");

    var collectAfterPlatform = await admin.PutAsJsonAsync("/api/v1/settings/collect", new
    {
        group = "collect",
        values = new Dictionary<string, string> { ["autoCollectOnConnect"] = "true" }
    });
    Pass("平台版只读拦截", collectAfterPlatform.StatusCode == HttpStatusCode.Forbidden,
        $"{(int)collectAfterPlatform.StatusCode}");

    // ---------- 6. 运行时文件持久化（重启后生效） ----------
    var runtimeJson = File.ReadAllText(runtimeFile);
    Pass("运行时文件持久化",
        runtimeJson.Contains("FileExtensions") &&
        runtimeJson.Contains(".mov") &&
        !runtimeJson.Contains(".log") &&
        runtimeJson.Contains("5126"),
        $"runtimeFile={File.Exists(runtimeFile)}, len={runtimeJson.Length}");

    // ---------- 7. 设备自检 + 报告导出 ----------
    var selfCheck = await admin.PostAsync("/api/v1/settings/self-check", null);
    var selfCheckItems = await selfCheck.Content.ReadFromJsonAsync<Resp<SelfCheckItemDto[]>>();
    Pass("设备自检（4 项）", selfCheck.IsSuccessStatusCode && selfCheckItems?.Data?.Length == 4,
        string.Join("; ", (selfCheckItems?.Data ?? []).Select(i => $"{i.Name}:{i.Ok}")));

    var report = await admin.GetAsync("/api/v1/settings/self-check/report?format=pdf");
    var reportBytes = await report.Content.ReadAsByteArrayAsync();
    Pass("自检报告 PDF 导出", report.IsSuccessStatusCode && reportBytes.Length > 100 &&
                              reportBytes[0] == 0x25 && reportBytes[1] == 0x50, // %P
        $"bytes={reportBytes.Length}");

    // ---------- 8. 权限：部门负责人可看不可改；操作员无设置权限 ----------
    using var manager = await Login(WebPort, "mgr", "Test@123");
    var managerGet = await manager.GetAsync("/api/v1/settings");
    var managerPut = await manager.PutAsJsonAsync("/api/v1/settings/collect", new
    {
        group = "collect",
        values = new Dictionary<string, string> { ["autoCollectOnConnect"] = "true" }
    });
    Pass("部门负责人只读", managerGet.StatusCode == HttpStatusCode.OK &&
                          managerPut.StatusCode == HttpStatusCode.Forbidden,
        $"get={(int)managerGet.StatusCode}, put={(int)managerPut.StatusCode}");

    using var op = await Login(WebPort, "op", "Test@123");
    var opGet = await op.GetAsync("/api/v1/settings");
    Pass("操作员无设置权限", opGet.StatusCode == HttpStatusCode.Forbidden, $"get={(int)opGet.StatusCode}");

    // ---------- 9. 授权激活（无公钥 → 明确错误） ----------
    var licenseResp = await admin.PostAsJsonAsync("/api/v1/settings/license/activate", "not-a-license");
    Pass("授权激活（无效文件明确报错）", licenseResp.StatusCode == HttpStatusCode.BadRequest,
        await licenseResp.Content.ReadAsStringAsync());

    // ---------- 10. 重启后设置持久化（运行时文件覆盖默认配置） ----------
    await host.StopAsync();
    Environment.SetEnvironmentVariable("STATION__WEB__PORT", null);
using (var host2 = HostBuilderFactory.Create().Build())
{
    await host2.StartAsync();
        var ready = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < ready)
        {
            try
            {
                using var probe = new TcpClient();
                probe.Connect("127.0.0.1", 5126);
                break;
            }
            catch
            {
                await Task.Delay(300);
            }
        }

        using var admin2 = await Login(5126, "admin", "Admin@123");
        var restarted = (await admin2.GetFromJsonAsync<Resp<SettingsData>>("/api/v1/settings"))?.Data;
        Pass("重启后设置持久化",
            restarted?.Network?.WebPort == 5126 &&
            restarted?.Collect?.CollectAncillaryFiles == false &&
            restarted?.Basic?.RunMode == "platform" &&
            restarted?.ReadOnly == true &&
            restarted?.Network?.MaxFailedAttempts == 3,
            $"webPort={restarted?.Network?.WebPort}, ancillary={restarted?.Collect?.CollectAncillaryFiles}, " +
            $"runMode={restarted?.Basic?.RunMode}, readOnly={restarted?.ReadOnly}, maxFailed={restarted?.Network?.MaxFailedAttempts}");
    }
}
catch (Exception ex)
{
    Pass("Spike 执行", false, ex.ToString());
}
finally
{
    await host.StopAsync();
    try
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
    catch
    {
        // 清理失败不影响结果
    }
}

Console.WriteLine();
Console.WriteLine("================ 系统设置验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record SettingsData(
    BasicSettingsDto? Basic,
    StorageSettingsDto? Storage,
    CollectSettingsDto? Collect,
    LicenseSettingsDto? License,
    NetworkSettingsDto? Network,
    bool ReadOnly);

internal sealed record NetworkSettingsDto(
    int WebPort,
    int HttpsPort,
    bool EnableLan,
    bool EnableHttps,
    bool AllowHttp,
    string CertificateStatus,
    int MaxFailedAttempts,
    int LockoutMinutes);

internal sealed record HintsResp(IReadOnlyList<string>? Hints);

internal sealed record Resp<T>(bool Success, int Code, string Message, T? Data);
