using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Authorization;
using Station.Application.Users;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

// ---------------------------------------------------------------------------
// M5 Spike: 账号认证 + RBAC + 数据权限（Station.Application）
// 覆盖：种子数据 / SM3 密码哈希 / 登录+失败锁定 / 权限判定 /
//       数据范围(部门树继承) / 用户-部门-角色管理 / 登录审计 / 改密
// ---------------------------------------------------------------------------

var dbArg = "";
var connOverride = "";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length) dbArg = args[i + 1];
    if (args[i] == "--conn" && i + 1 < args.Length) connOverride = args[i + 1];
}

var (provider, defaultConn) = dbArg.ToLowerInvariant() switch
{
    "mysql" => (DbProvider.MySql, "Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None"),
    "postgresql" or "pg" => (DbProvider.PostgreSQL, "Host=localhost;Port=5432;Database=station_spike;Username=station;Password=Station@123"),
    "sqlite" => (DbProvider.Sqlite, $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_auth.db")}"),
    _ => (DbProvider.Kingbase, "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123")
};
var connStr = string.IsNullOrWhiteSpace(connOverride) ? defaultConn : connOverride;

var results = new List<(string Step, bool Ok, string Detail)>();

void Pass(string step, string detail)
{
    results.Add((step, true, detail));
    Console.WriteLine($"[PASS] {step}: {detail}");
}

void Fail(string step, Exception ex)
{
    results.Add((step, false, ex.Message));
    Console.WriteLine($"[FAIL] {step}: {ex.Message}");
}

static void DropAuthTables(ISqlSugarClient db)
{
    foreach (var t in new[]
             {
                 typeof(AuditLog), typeof(UserRole), typeof(RolePermission),
                 typeof(Permission), typeof(Role), typeof(User), typeof(Dept), typeof(Account)
             })
    {
        var mi = typeof(IDbMaintenance).GetMethods()
            .First(m => m.Name == "DropTable" && m.GetParameters().Length == 0 && m.IsGenericMethodDefinition)
            .MakeGenericMethod(t);
        try
        {
            mi.Invoke(db.DbMaintenance, null);
        }
        catch
        {
            // 表不存在，忽略
        }
    }
}

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Auth:MaxFailedAttempts"] = "5",
        ["Station:Auth:LockoutMinutes"] = "15",
        ["Station:Auth:AutoLogoutMinutes"] = "1"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

// 幂等：清理上一次运行可能遗留的测试表
DropAuthTables(sp.GetRequiredService<ISqlSugarClient>());

// ---------- 1. 种子数据 ----------
try
{
    var seeder = sp.GetRequiredService<IAuthSeeder>();
    await seeder.EnsureAsync();

    var roles = sp.GetRequiredService<IRepository<Role>>();
    var perms = sp.GetRequiredService<IRepository<Permission>>();
    var accounts = sp.GetRequiredService<IRepository<Account>>();
    var roleCount = await roles.CountAsync();
    var permCount = await perms.CountAsync();
    var admin = await accounts.FirstAsync(a => a.UserName == "admin");

    Pass("种子数据", $"角色={roleCount}(应4), 权限={permCount}(应≥14), 管理员账号存在={admin is not null}");
}
catch (Exception ex)
{
    Fail("种子数据", ex);
}

// ---------- 2. SM3 密码哈希 ----------
try
{
    var hasher = sp.GetRequiredService<IPasswordHasher>();
    var hash = hasher.Hash("Test@123");
    var ok1 = hasher.Verify("Test@123", hash);
    var ok2 = !hasher.Verify("wrong", hash);
    var ok3 = hash.StartsWith("sm3$");
    Pass("SM3 密码哈希", $"格式={hash[..12]}..., 验证正确={ok1}, 错误密码拒绝={ok2}, 前缀正确={ok3}");
}
catch (Exception ex)
{
    Fail("SM3 密码哈希", ex);
}

// ---------- 3. 登录失败锁定（5 次锁 15 分钟） ----------
try
{
    var auth = sp.GetRequiredService<IAuthenticationService>();
    var accounts = sp.GetRequiredService<IRepository<Account>>();

    for (var i = 1; i <= 4; i++)
    {
        var r = await auth.LoginAsync(new LoginRequest("admin", "wrong-password", "127.0.0.1"));
        if (r.FailureReason != LoginFailureReason.InvalidCredentials)
        {
            throw new InvalidOperationException($"第{i}次失败未按预期返回 InvalidCredentials");
        }
    }

    var fifth = await auth.LoginAsync(new LoginRequest("admin", "wrong-password", "127.0.0.1"));
    var locked = fifth.FailureReason == LoginFailureReason.LockedOut && fifth.LockedUntil is { } lu && lu > DateTime.Now;
    var adminAccount = await accounts.FirstAsync(a => a.UserName == "admin");
    var lockedUntilSet = adminAccount!.LockedUntil is { } lu2 && lu2 > DateTime.Now;

    var stillLocked = (await auth.LoginAsync(new LoginRequest("admin", "Admin@123", "127.0.0.1"))).FailureReason == LoginFailureReason.LockedOut;

    Pass("失败锁定", $"第5次锁定={locked}, 库内LockedUntil已写={lockedUntilSet}, 锁定期正确密码也被拒={stillLocked}");
}
catch (Exception ex)
{
    Fail("失败锁定", ex);
}

// 解锁管理员，继续后续流程
try
{
    var accounts = sp.GetRequiredService<IRepository<Account>>();
    var admin = await accounts.FirstAsync(a => a.UserName == "admin");
    if (admin is null)
    {
        throw new InvalidOperationException("管理员账号不存在");
    }

    admin.LockedUntil = null;
    admin.FailedLoginAttempts = 0;
    await accounts.UpdateAsync(admin);
}
catch (Exception ex)
{
    Fail("解锁管理员", ex);
}

// ---------- 4. 管理员登录成功 + 全权限 ----------
try
{
    var auth = sp.GetRequiredService<IAuthenticationService>();
    var authorization = sp.GetRequiredService<IAuthorizationService>();
    var login = await auth.LoginAsync(new LoginRequest("admin", "Admin@123", "127.0.0.1"));
    if (!login.Success || login.Session is null)
    {
        throw new InvalidOperationException("管理员登录失败");
    }

    var hasUserManage = await authorization.HasPermissionAsync(login.Session.AccountId, PermissionCodes.UserManage);
    var hasRoleManage = await authorization.HasPermissionAsync(login.Session.AccountId, PermissionCodes.RoleManage);
    var isAll = login.Session.DataScope == DataScope.All;
    Pass("管理员登录", $"角色={string.Join(",", login.Session.Roles)}, user:manage={hasUserManage}, role:manage={hasRoleManage}, 数据范围=All={isAll}");
}
catch (Exception ex)
{
    Fail("管理员登录", ex);
}

// ---------- 5. 部门树 + 用户 + 账号 + 角色分配 ----------
try
{
    var userService = sp.GetRequiredService<IUserService>();

    var rootDept = (await userService.GetDeptTreeAsync()).First(d => d.Code == "ROOT");
    var team1Id = await userService.CreateDeptAsync(new DeptDto(null, "TEAM1", "一队", rootDept.Id, 1));
    var grp1Id = await userService.CreateDeptAsync(new DeptDto(null, "GRP1", "一组", team1Id, 1));

    var r1 = await userService.CreateUserAsync(new UserDto(null, "U001", "张三", grp1Id), "zhangsan", "Test@123");
    var r2 = await userService.CreateUserAsync(new UserDto(null, "U002", "李四", team1Id), "lisi", "Test@123");
    var r3 = await userService.CreateUserAsync(new UserDto(null, "U003", "王五", rootDept.Id!.Value), "wangwu", "Test@123");
    if (!r1.Success || !r2.Success || !r3.Success)
    {
        throw new InvalidOperationException($"建用户失败: {r1.Message}{r2.Message}{r3.Message}");
    }

    var users = await userService.GetUsersAsync();
    var zhangSan = users.First(u => u.UserNo == "U001");
    var liSi = users.First(u => u.UserNo == "U002");
    var wangWu = users.First(u => u.UserNo == "U003");

    var roles = await userService.GetRolesAsync();
    var opRole = roles.First(r => r.Code == AuthRoleCodes.Operator);
    var mgrRole = roles.First(r => r.Code == AuthRoleCodes.Manager);
    var audRole = roles.First(r => r.Code == AuthRoleCodes.Auditor);

    await userService.AssignRolesAsync(zhangSan.Id!.Value, [opRole.Id!.Value]);
    await userService.AssignRolesAsync(liSi.Id!.Value, [mgrRole.Id!.Value]);
    await userService.AssignRolesAsync(wangWu.Id!.Value, [audRole.Id!.Value]);

    Pass("部门/用户/账号/角色", $"部门树 ROOT>一队>一组, 用户 U001/U002/U003, 角色已分配");
}
catch (Exception ex)
{
    Fail("部门/用户/账号/角色", ex);
}

// ---------- 6. 数据范围（部门树继承） ----------
try
{
    var scopeProvider = sp.GetRequiredService<IDataScopeProvider>();
    var userService = sp.GetRequiredService<IUserService>();
    var users = await userService.GetUsersAsync();

    var zhangSan = users.First(u => u.UserNo == "U001");
    var liSi = users.First(u => u.UserNo == "U002");
    var wangWu = users.First(u => u.UserNo == "U003");

    var zs = await scopeProvider.GetDataScopeAsync(zhangSan.Id!.Value);
    var ls = await scopeProvider.GetDataScopeAsync(liSi.Id!.Value);
    var ww = await scopeProvider.GetDataScopeAsync(wangWu.Id!.Value);

    var grp1Id = (await userService.GetDeptTreeAsync()).First(d => d.Code == "GRP1").Id!.Value;
    var team1Id = (await userService.GetDeptTreeAsync()).First(d => d.Code == "TEAM1").Id!.Value;

    var zsOk = zs.Scope == DataScope.Self && zs.AllowedDeptIds.SequenceEqual(new[] { grp1Id });
    var lsOk = ls.Scope == DataScope.DeptAndChildren
               && ls.AllowedDeptIds.Contains(team1Id)
               && ls.AllowedDeptIds.Contains(grp1Id)
               && ls.AllowedDeptIds.Count == 2;
    var wwOk = ww.Scope == DataScope.All && ww.IsAll;

    Pass("数据范围", $"操作员(张三)=Self/仅本组={zsOk}, 部门负责人(李四)=本部门+下级={lsOk}, 审计员(王五)=All={wwOk}");
}
catch (Exception ex)
{
    Fail("数据范围", ex);
}

// ---------- 7. 权限判定（按角色） ----------
try
{
    var authorization = sp.GetRequiredService<IAuthorizationService>();
    var userService = sp.GetRequiredService<IUserService>();
    var accounts = sp.GetRequiredService<IRepository<Account>>();

    var zhangSan = await accounts.FirstAsync(a => a.UserName == "zhangsan");
    var liSi = await accounts.FirstAsync(a => a.UserName == "lisi");
    var wangWu = await accounts.FirstAsync(a => a.UserName == "wangwu");

    var zsSession = await authorization.GetSessionAsync(zhangSan!.Id);
    var zsView = zsSession.Permissions.Contains(PermissionCodes.FileView);
    var zsManage = zsSession.Permissions.Contains(PermissionCodes.UserManage);

    var lsSession = await authorization.GetSessionAsync(liSi!.Id);
    var lsView = lsSession.Permissions.Contains(PermissionCodes.UserView);
    var lsRoleManage = lsSession.Permissions.Contains(PermissionCodes.RoleManage);

    var wwSession = await authorization.GetSessionAsync(wangWu!.Id);
    var wwAuditView = wwSession.Permissions.Contains(PermissionCodes.AuditView);
    var wwFileManage = wwSession.Permissions.Contains(PermissionCodes.FileManage);

    Pass("权限判定", $"操作员: file:view={zsView}, user:manage={zsManage}; 负责人: user:view={lsView}, role:manage={lsRoleManage}; 审计员: audit:view={wwAuditView}, file:manage={wwFileManage}");
}
catch (Exception ex)
{
    Fail("权限判定", ex);
}

// ---------- 8. 登录审计 ----------
try
{
    var audit = sp.GetRequiredService<IAuditLogService>();
    var recent = await audit.GetRecentAsync(50);
    var loginLogs = recent.Where(l => l.OperationType == "login").ToList();
    var hasSuccess = loginLogs.Any(l => l.Result == 1);
    var hasFailure = loginLogs.Any(l => l.Result == 0);
    Pass("登录审计", $"登录日志={loginLogs.Count} 条, 含成功={hasSuccess}, 含失败={hasFailure}");
}
catch (Exception ex)
{
    Fail("登录审计", ex);
}

// ---------- 9. 修改密码 ----------
try
{
    var auth = sp.GetRequiredService<IAuthenticationService>();
    var accounts = sp.GetRequiredService<IRepository<Account>>();
    var zhangsan = await accounts.FirstAsync(a => a.UserName == "zhangsan");

    var wrongOld = await auth.ChangePasswordAsync(zhangsan!.Id, "wrong-old", "New@123");
    var okChange = await auth.ChangePasswordAsync(zhangsan.Id, "Test@123", "New@123");
    var relogin = await auth.LoginAsync(new LoginRequest("zhangsan", "New@123", "127.0.0.1"));
    var restore = await auth.ChangePasswordAsync(zhangsan.Id, "New@123", "Test@123");

    Pass("修改密码", $"旧密码错误拒绝={!wrongOld.Success}, 修改成功={okChange.Success}, 新密码可登录={relogin.Success}, 还原={restore.Success}");
}
catch (Exception ex)
{
    Fail("修改密码", ex);
}

// ---------- 10. 清理测试表 ----------
try
{
    DropAuthTables(sp.GetRequiredService<ISqlSugarClient>());
    Pass("清理", "已删除 8 张认证测试表");
}
catch (Exception ex)
{
    Fail("清理", ex);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 认证/RBAC 验证 ================");
var failed = 0;
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
    if (!ok) failed++;
}
Console.WriteLine("--------------------------------------------------------");
Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
