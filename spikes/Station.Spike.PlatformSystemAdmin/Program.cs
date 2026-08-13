using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SqlSugar;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M28 Spike: 平台系统管理（组织架构/用户/角色/审计日志）
//  - CRUD 全链路 + RBAC 权限码 + 部门树数据范围
//  - 事务创建用户/角色、审计写入、预置角色只读、删除引用保护
// ---------------------------------------------------------------------------

var platformMysql = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

long operatorRoleId = 0;
long managerRoleId = 0;
long wangwuId = 0;
long customRoleId = 0;
long grp2Id = 0;

using (var seed = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = platformMysql,
    DbType = DbType.MySql,
    IsAutoCloseConnection = true
}))
{
    seed.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('dept.create','dept.update','dept.delete','user.create','user.update','user.reset-password','role.create','role.update')");
    seed.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu')");
    seed.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu'))");
    seed.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu')");
    seed.Ado.ExecuteCommand("delete from station_role_permission where RoleId in (select Id from station_role where Code='custom_tester')");
    seed.Ado.ExecuteCommand("delete from station_role where Code='custom_tester'");
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
    operatorRoleId = seed.Queryable<Role>().Where(r => r.Code == "operator").First().Id;
    managerRoleId = seed.Queryable<Role>().Where(r => r.Code == "manager").First().Id;
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = zhangSan.Id, RoleId = operatorRoleId }).ExecuteCommand();
    seed.Insertable(new UserRole { Id = id.NextId(), UserId = liSi.Id, RoleId = managerRoleId }).ExecuteCommand();

    var hasher = new Sm3PasswordHasher();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "zhangsan", PasswordHash = hasher.Hash("Test@123"), UserId = zhangSan.Id }).ExecuteCommand();
    seed.Insertable(new Account { Id = id.NextId(), UserName = "lisi", PasswordHash = hasher.Hash("Test@123"), UserId = liSi.Id }).ExecuteCommand();
}

const int PlatformPort = 5117;
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

static JsonElement FindItem(JsonElement page, string field, string value)
{
    var items = page.GetProperty("data").GetProperty("items").EnumerateArray();
    return items.First(x => x.GetProperty(field).GetString() == value);
}

try
{
    // ---------- 0. 未登录访问用户管理 -> 401 ----------
    try
    {
        using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
        var resp = await anon.GetAsync("/api/v1/users?page=1&size=10");
        Pass("未登录用户管理401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录用户管理401", false, ex.Message);
    }

    using var admin = await Login(PlatformPort, "admin", "Admin@123");

    // ---------- 1. 部门创建 + 数据范围 ----------
    try
    {
        var team2 = await admin.PostAsJsonAsync("/api/v1/depts", new { code = "TEAM2", name = "二队", parentId = 1, sortOrder = 2 });
        using (var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            grp2Id = db.Queryable<Dept>().Where(d => d.Code == "TEAM2").First().Id;
        }

        var grp2 = await admin.PostAsJsonAsync("/api/v1/depts", new { code = "GRP2", name = "二组", parentId = grp2Id, sortOrder = 1 });
        var depts = await GetJson(admin, "/api/v1/depts");
        var codes = depts.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToList();
        Pass("部门创建与列表", team2.StatusCode == HttpStatusCode.OK && grp2.StatusCode == HttpStatusCode.OK &&
                            codes.Contains("TEAM2") && codes.Contains("GRP2"),
            $"TEAM2/GRP2 已创建，列表含 {codes.Count} 个部门");
    }
    catch (Exception ex)
    {
        Pass("部门创建与列表", false, ex.Message);
    }

    // ---------- 2. 负责人数据范围：只见本部门及下级；无管理权限 ----------
    try
    {
        using var manager = await Login(PlatformPort, "lisi", "Test@123");
        var depts = await GetJson(manager, "/api/v1/depts");
        var codes = depts.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToList();
        var denied = await manager.PostAsJsonAsync("/api/v1/depts", new { code = "TEAM3", name = "三队", parentId = 1 });
        Pass("负责人部门范围与权限", codes.Contains("TEAM1") && codes.Contains("GRP1") &&
                                  !codes.Contains("TEAM2") && !codes.Contains("ROOT") &&
                                  denied.StatusCode == HttpStatusCode.Forbidden,
            $"可见={string.Join(",", codes)}, 新建={denied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("负责人部门范围与权限", false, ex.Message);
    }

    // ---------- 3. 用户创建（事务：用户+账号+角色） ----------
    try
    {
        var created = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            userNo = "wangwu",
            name = "王五",
            deptId = grp2Id,
            password = "Test@123",
            roleIds = new[] { operatorRoleId }
        });
        var users = await GetJson(admin, "/api/v1/users?page=1&size=50&keyword=wangwu");
        var wangwu = FindItem(users, "userNo", "wangwu");
        wangwuId = wangwu.GetProperty("id").GetInt64();
        var roles = wangwu.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToList();
        Pass("用户创建与列表", created.StatusCode == HttpStatusCode.OK && roles.Contains("操作员"),
            $"roles={string.Join(",", roles)}");
    }
    catch (Exception ex)
    {
        Pass("用户创建与列表", false, ex.Message);
    }

    // ---------- 4. 新用户可登录；操作员无系统管理权限 ----------
    try
    {
        using var wangwuClient = await Login(PlatformPort, "wangwu", "Test@123");
        var usersResp = await wangwuClient.GetAsync("/api/v1/users?page=1&size=10");
        var deptsResp = await wangwuClient.GetAsync("/api/v1/depts");
        Pass("新用户登录+权限拦截", usersResp.StatusCode == HttpStatusCode.Forbidden &&
                                   deptsResp.StatusCode == HttpStatusCode.Forbidden,
            $"users={usersResp.StatusCode}, depts={deptsResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("新用户登录+权限拦截", false, ex.Message);
    }

    // ---------- 5. 用户编辑（换角色） + 重置密码 ----------
    try
    {
        var updated = await admin.PutAsJsonAsync($"/api/v1/users/{wangwuId}", new
        {
            name = "王五",
            deptId = grp2Id,
            isActive = true,
            roleIds = new[] { managerRoleId }
        });
        var reset = await admin.PostAsJsonAsync($"/api/v1/users/{wangwuId}/reset-password", new { password = "New@123" });
        var users = await GetJson(admin, "/api/v1/users?page=1&size=50&keyword=wangwu");
        var wangwu = FindItem(users, "userNo", "wangwu");
        var roles = wangwu.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToList();
        using var relogin = await Login(PlatformPort, "wangwu", "New@123");
        Pass("用户编辑+重置密码", updated.StatusCode == HttpStatusCode.OK &&
                                 reset.StatusCode == HttpStatusCode.OK &&
                                 roles.Contains("部门负责人") && !roles.Contains("操作员"),
            $"roles={string.Join(",", roles)}, 新密码登录OK");
    }
    catch (Exception ex)
    {
        Pass("用户编辑+重置密码", false, ex.Message);
    }

    // ---------- 6. 角色创建/编辑；预置角色只读 ----------
    try
    {
        var created = await admin.PostAsJsonAsync("/api/v1/roles", new
        {
            code = "custom_tester",
            name = "测试角色",
            dataScope = 2,
            permissionCodes = new[] { "file:view", "alert:view" }
        });
        var roles = await GetJson(admin, "/api/v1/roles");
        var custom = roles.GetProperty("data").EnumerateArray().First(x => x.GetProperty("code").GetString() == "custom_tester");
        customRoleId = custom.GetProperty("id").GetInt64();
        var perms2 = custom.GetProperty("permissions").EnumerateArray().Count();

        var updated = await admin.PutAsJsonAsync($"/api/v1/roles/{customRoleId}", new
        {
            name = "测试角色",
            dataScope = 2,
            isActive = true,
            permissionCodes = new[] { "file:view", "alert:view", "audit:view" }
        });
        var roles2 = await GetJson(admin, "/api/v1/roles");
        var custom2 = roles2.GetProperty("data").EnumerateArray().First(x => x.GetProperty("code").GetString() == "custom_tester");
        var perms3 = custom2.GetProperty("permissions").EnumerateArray().Count();

        var adminRoleId = 0L;
        using (var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = platformMysql,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }))
        {
            adminRoleId = db.Queryable<Role>().Where(r => r.Code == "admin").First().Id;
        }

        var sysDenied = await admin.PutAsJsonAsync($"/api/v1/roles/{adminRoleId}", new
        {
            name = "管理员X",
            dataScope = 0,
            isActive = true,
            permissionCodes = Array.Empty<string>()
        });
        Pass("角色创建/编辑/预置只读", created.StatusCode == HttpStatusCode.OK &&
                                      perms2 == 2 && perms3 == 3 &&
                                      updated.StatusCode == HttpStatusCode.OK &&
                                      sysDenied.StatusCode == HttpStatusCode.BadRequest,
            $"权限数 2→3, 修改预置角色={(int)sysDenied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("角色创建/编辑/预置只读", false, ex.Message);
    }

    // ---------- 7. 删除保护：部门下有用户拒绝删除 ----------
    try
    {
        var denied = await admin.DeleteAsync($"/api/v1/depts/{grp2Id}");
        Pass("删除引用保护", denied.StatusCode == HttpStatusCode.BadRequest, $"HTTP {(int)denied.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("删除引用保护", false, ex.Message);
    }

    // ---------- 8. 审计日志：写入 + 查询 + 数据范围 ----------
    try
    {
        var logs = await GetJson(admin, "/api/v1/audit-logs?page=1&size=20&keyword=TEAM2");
        var total = logs.GetProperty("data").GetProperty("totalCount").GetInt64();
        var hasDeptCreate = logs.GetProperty("data").GetProperty("items").EnumerateArray()
            .Any(x => x.GetProperty("operationType").GetString() == "dept.create" &&
                      x.GetProperty("target").GetString() == "TEAM2");
        var userLogs = await GetJson(admin, "/api/v1/audit-logs?page=1&size=20&keyword=wangwu");
        var hasUserCreate = userLogs.GetProperty("data").GetProperty("items").EnumerateArray()
            .Any(x => x.GetProperty("operationType").GetString() == "user.create");

        using var manager = await Login(PlatformPort, "lisi", "Test@123");
        var managerLogs = await GetJson(manager, "/api/v1/audit-logs?page=1&size=20&keyword=TEAM2");
        var managerTotal = managerLogs.GetProperty("data").GetProperty("totalCount").GetInt64();
        Pass("审计写入/查询/数据范围", total >= 1 && hasDeptCreate && hasUserCreate && managerTotal == 0,
            $"admin见{total}条(dept.create={hasDeptCreate}, user.create={hasUserCreate}), 负责人见{managerTotal}条");
    }
    catch (Exception ex)
    {
        Pass("审计写入/查询/数据范围", false, ex.Message);
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
    clean.Ado.ExecuteCommand("delete from station_audit_log where OperationType in ('dept.create','dept.update','dept.delete','user.create','user.update','user.reset-password','role.create','role.update')");
    clean.Ado.ExecuteCommand("delete from station_account where UserName in ('zhangsan','lisi','wangwu')");
    clean.Ado.ExecuteCommand("delete from station_user_role where UserId in (select Id from station_user where UserNo in ('zhangsan','lisi','wangwu'))");
    clean.Ado.ExecuteCommand("delete from station_user where UserNo in ('zhangsan','lisi','wangwu')");
    clean.Ado.ExecuteCommand("delete from station_role_permission where RoleId in (select Id from station_role where Code='custom_tester')");
    clean.Ado.ExecuteCommand("delete from station_role where Code='custom_tester'");
    clean.Ado.ExecuteCommand("delete from station_dept where Code in ('TEAM1','GRP1','TEAM2','GRP2')");
}

Console.WriteLine();
Console.WriteLine("================ M28 平台系统管理 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
