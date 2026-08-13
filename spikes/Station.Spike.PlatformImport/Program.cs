using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SqlSugar;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M32 Spike: 组织/用户 CSV 导入（模板校验、逐行校验、部分成功 + 错误报告）
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'import.%'");
    seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu','zhaoliu')");
    seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu','zhaoliu'))");
    seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu','zhaoliu')");
    seed.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1','TEAM2','GRP2')");

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
}

const int PlatformPort = 5121;
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

static async Task<JsonElement> ImportCsv(HttpClient http, string url, string csv)
{
    var resp = await http.PostAsync(url, new StringContent(csv, Encoding.UTF8, "text/plain"));
    if (!resp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"导入 {url} -> {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
    }

    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
}

try
{
    // ---------- 0. 未登录导入 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.PostAsync("/api/v1/imports/depts", new StringContent("编码,名称\nX,测试", Encoding.UTF8, "text/plain"));
        Pass("未登录导入401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录导入401", false, ex.Message);
    }

    using var admin = await Login(PlatformPort, "admin", "Admin@123");

    // ---------- 1. 部门导入：2 成功 + 2 失败（上级不存在/缺编码名称） ----------
    try
    {
        var deptCsv = "编码,名称,上级编码,排序\nTEAM2,二队,,\nGRP2,二组,TEAM2,1\nBAD,坏行,NO_PARENT,1\n,无名,,\n";
        var result = await ImportCsv(admin, "/api/v1/imports/depts", deptCsv);
        var data = result.GetProperty("data");
        var errors = data.GetProperty("errors").EnumerateArray().Select(x => $"{x.GetProperty("line").GetInt32()}:{x.GetProperty("message").GetString()}").ToList();
        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var team2 = check.Queryable<Dept>().Where(d => d.Code == "TEAM2").First();
            var grp2 = check.Queryable<Dept>().Where(d => d.Code == "GRP2").First();
            Pass("部门导入", data.GetProperty("success").GetInt32() == 2 &&
                            data.GetProperty("failed").GetInt32() == 2 &&
                            errors.Count == 2 && errors[0].StartsWith("4:") && errors[1].StartsWith("5:") &&
                            team2 is not null && grp2 is not null && grp2.ParentId == team2.Id,
                $"成功{data.GetProperty("success").GetInt32()}/失败{data.GetProperty("failed").GetInt32()}, 错误={string.Join(";", errors)}");
        }
    }
    catch (Exception ex)
    {
        Pass("部门导入", false, ex.Message);
    }

    // ---------- 2. 用户导入：2 成功（含角色/无角色）+ 2 失败（坏部门/重复工号） ----------
    try
    {
        var userCsv = "工号,姓名,部门编码,角色编码,初始密码\nwangwu,王五,GRP2,operator,\nzhaoliu,赵六,GRP2,,\nbaduser,坏用户,BAD_DEPT,,\nzhangsan,重复,TEAM1,,\n";
        var result = await ImportCsv(admin, "/api/v1/imports/users", userCsv);
        var data = result.GetProperty("data");
        var errors = data.GetProperty("errors").EnumerateArray().Select(x => $"{x.GetProperty("line").GetInt32()}:{x.GetProperty("message").GetString()}").ToList();

        using (var check = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            var wangwu = check.Queryable<User>().Where(u => u.UserNo == "wangwu").First();
            var zhaoliu = check.Queryable<User>().Where(u => u.UserNo == "zhaoliu").First();
            var wangwuRoleCount = check.Queryable<UserRole>().Where(ur => ur.UserId == wangwu.Id).Count();
            var zhaoliuRoleCount = check.Queryable<UserRole>().Where(ur => ur.UserId == zhaoliu.Id).Count();
            using var wangwuClient = await Login(PlatformPort, "wangwu", "Station@123");
            Pass("用户导入", data.GetProperty("success").GetInt32() == 2 &&
                            data.GetProperty("failed").GetInt32() == 2 &&
                            errors[0].StartsWith("4:") && errors[1].StartsWith("5:") &&
                            wangwuRoleCount == 1 && zhaoliuRoleCount == 0 &&
                            wangwuClient is not null,
                $"成功{data.GetProperty("success").GetInt32()}/失败{data.GetProperty("failed").GetInt32()}, wangwu角色={wangwuRoleCount}, zhaoliu角色={zhaoliuRoleCount}, 默认密码登录OK");
        }
    }
    catch (Exception ex)
    {
        Pass("用户导入", false, ex.Message);
    }

    // ---------- 3. 权限：负责人/操作员无导入权限 ----------
    try
    {
        using var manager = await Login(PlatformPort, "lisi", "Test@123");
        var managerResp = await manager.PostAsync("/api/v1/imports/depts", new StringContent("编码,名称\nX,测试", Encoding.UTF8, "text/plain"));
        using var op = await Login(PlatformPort, "zhangsan", "Test@123");
        var opResp = await op.PostAsync("/api/v1/imports/users", new StringContent("工号,姓名,部门编码\nx,测试,TEAM1", Encoding.UTF8, "text/plain"));
        Pass("导入权限", managerResp.StatusCode == HttpStatusCode.Forbidden && opResp.StatusCode == HttpStatusCode.Forbidden,
            $"负责人={(int)managerResp.StatusCode}, 操作员={(int)opResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("导入权限", false, ex.Message);
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
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType like 'import.%'");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu','zhaoliu')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu','zhaoliu'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu','zhaoliu')");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1','TEAM2','GRP2')");
}

Console.WriteLine();
Console.WriteLine("================ M32 组织/用户 CSV 导入 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
