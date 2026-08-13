using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M38 Spike: 单机 Web 补齐（局域网/HTTPS 配置 + 访问日志 + CSV 导入）
// ---------------------------------------------------------------------------

Environment.CurrentDirectory = @"E:\Reny\station\src\Station.Desktop\Station.Desktop.WebHost";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

static bool HasListener(IPAddress address, int port) =>
    IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
        .Any(e => e.Port == port && e.Address.Equals(address));

static async Task WaitWebAsync(int port)
{
    var deadline = DateTime.Now.AddSeconds(30);
    while (DateTime.Now < deadline)
    {
        try
        {
            using var probe = new TcpClient();
            probe.Connect("127.0.0.1", port);
            return;
        }
        catch
        {
            await Task.Delay(300);
        }
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

static void Seed(string dbFile)
{
    using var db = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={dbFile}",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    });
    var id = new SnowflakeIdGenerator();
    var team1 = new Dept { Id = id.NextId(), Code = "TEAM1", Name = "一队", ParentId = 1, SortOrder = 1 };
    db.Insertable(team1).ExecuteCommand();
    var hasher = new Sm3PasswordHasher();
    db.Insertable(new Recorder { Id = id.NextId(), SerialNumber = "R-BIND", Model = "M-X", Protocol = Station.Contracts.ProtocolType.Ums, IsAuthorized = true, IsActive = true }).ExecuteCommand();
}

// ================= Phase 1：默认本机（EnableLan=false）+ 导入 =================
const int LanOffPort = 5125;
var db1 = Path.Combine(Path.GetTempPath(), $"station-m38-{Guid.NewGuid():N}.db");
Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={db1}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", Path.Combine(Path.GetTempPath(), "station-m38-cache1"));
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");
Environment.SetEnvironmentVariable("STATION__WEB__PORT", LanOffPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");

using (var host1 = HostBuilderFactory.Create().Build())
{
    await host1.StartAsync();
    await WaitWebAsync(LanOffPort);
    Seed(db1);

    // ---------- 1. 默认仅本机监听 ----------
    try
    {
        Pass("默认本机监听", HasListener(IPAddress.Loopback, LanOffPort) && !HasListener(IPAddress.Any, LanOffPort),
            $"127.0.0.1:{LanOffPort}");
    }
    catch (Exception ex)
    {
        Pass("默认本机监听", false, ex.Message);
    }

    // ---------- 2. 部门/用户/记录仪绑定导入 ----------
    try
    {
        using var admin = await Login(LanOffPort, "admin", "Admin@123");
        var deptCsv = "编码,名称,上级编码,排序\nTEAM2,二队,,\nGRP2,二组,TEAM2,1\nBAD,坏行,NO_PARENT,1\n";
        var deptResp = await admin.PostAsync("/api/v1/imports/depts", new StringContent(deptCsv, Encoding.UTF8, "text/plain"));
        var deptResult = await deptResp.Content.ReadFromJsonAsync<ImportResponse>();

        var userCsv = "工号,姓名,部门编码,角色编码,初始密码\nwangwu,王五,TEAM2,operator,\nbaduser,坏用户,BAD,,\n";
        var userResp = await admin.PostAsync("/api/v1/imports/users", new StringContent(userCsv, Encoding.UTF8, "text/plain"));
        var userResult = await userResp.Content.ReadFromJsonAsync<ImportResponse>();

        var bindCsv = "序列号,用户工号,部门编码\nR-BIND,wangwu,\n";
        var bindResp = await admin.PostAsync("/api/v1/imports/recorder-bindings", new StringContent(bindCsv, Encoding.UTF8, "text/plain"));
        var bindResult = await bindResp.Content.ReadFromJsonAsync<ImportResponse>();

        using var wangwu = await Login(LanOffPort, "wangwu", "Station@123");
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={db1}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true
        }))
        {
            var wangwuUser = check.Queryable<User>().Where(u => u.UserNo == "wangwu").First();
            var recorder = check.Queryable<Recorder>().Where(r => r.SerialNumber == "R-BIND").First();
            Pass("CSV导入", deptResult!.Data!.Success == 2 && deptResult.Data.Failed == 1 &&
                           userResult!.Data!.Success == 1 && userResult.Data.Failed == 1 &&
                           bindResult!.Data!.Success == 1 &&
                           recorder.BoundUserId == wangwuUser.Id &&
                           wangwu is not null,
                $"部门={deptResult.Data.Success}/{deptResult.Data.Failed}, 用户={userResult.Data.Success}/{userResult.Data.Failed}, 绑定={bindResult.Data.Success}, 默认密码登录OK");
        }
    }
    catch (Exception ex)
    {
        Pass("CSV导入", false, ex.ToString());
    }

    // ---------- 3. 默认模式不写访问日志 ----------
    try
    {
        using var admin = await Login(LanOffPort, "admin", "Admin@123");
        var logs = await admin.GetFromJsonAsync<AuditPage>("/api/v1/audit-logs?page=1&size=50");
        var hasAccess = logs!.Data!.Items!.Any(x => x.Target == "/api/v1/audit-logs" && x.OperationType == "web.access");
        Pass("默认模式无访问日志", !hasAccess, $"web.access 条数=0");
    }
    catch (Exception ex)
    {
        Pass("默认模式无访问日志", false, ex.Message);
    }

    await host1.StopAsync();
}

// ================= Phase 2：开启局域网（EnableLan=true）+ 访问日志 =================
const int LanOnPort = 5126;
var db2 = Path.Combine(Path.GetTempPath(), $"station-m38-{Guid.NewGuid():N}.db");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={db2}");
Environment.SetEnvironmentVariable("STATION__WEB__PORT", LanOnPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "true");

using (var host2 = HostBuilderFactory.Create().Build())
{
    await host2.StartAsync();
    await WaitWebAsync(LanOnPort);
    Seed(db2);

    try
    {
        Pass("局域网监听0.0.0.0", HasListener(IPAddress.Any, LanOnPort), $"0.0.0.0:{LanOnPort}");
    }
    catch (Exception ex)
    {
        Pass("局域网监听0.0.0.0", false, ex.Message);
    }

    try
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{LanOnPort}") };
        await http.GetAsync("/api/v1/health");
        using var admin = await Login(LanOnPort, "admin", "Admin@123");
        var logs = await admin.GetFromJsonAsync<AuditPage>("/api/v1/audit-logs?page=1&size=50");
        var hasHealthAccess = logs!.Data!.Items!.Any(x =>
            x.OperationType == "web.access" && x.Target == "/api/v1/health" && x.SourceIp == "127.0.0.1");
        Pass("局域网访问日志", hasHealthAccess, "web.access 含 /api/v1/health 且来源 127.0.0.1");
    }
    catch (Exception ex)
    {
        Pass("局域网访问日志", false, ex.ToString());
    }

    await host2.StopAsync();
}

Console.WriteLine();
Console.WriteLine("================ M38 单机 Web 补齐 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed record ImportResponse(bool Success, int Code, string Message, ImportData? Data);

internal sealed record ImportData(int Total, int Success, int Failed, List<object>? Errors);

internal sealed record AuditPage(bool Success, int Code, string Message, AuditData? Data);

internal sealed record AuditData(int PageIndex, int PageSize, long TotalCount, List<AuditItem>? Items);

internal sealed record AuditItem(long Id, string? OperatorAccount, string? OperatorName, long? DeptId, string? SourceIp, string OperationType, string? Target, string? Detail, int Result, DateTime CreatedAt);
