using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Alerts;
using Station.Application.Authorization;
using Station.Application.Users;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;

// ---------------------------------------------------------------------------
// M10 Spike: 报警中心（写入/筛选/状态流转 + 权限映射）
// ---------------------------------------------------------------------------

var dbArg = "sqlite";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length) dbArg = args[i + 1];
}

var provider = dbArg.ToLowerInvariant() switch
{
    "kingbase" => DbProvider.Kingbase,
    "mysql" => DbProvider.MySql,
    "postgresql" or "pg" => DbProvider.PostgreSQL,
    _ => DbProvider.Sqlite
};
var connStr = provider switch
{
    DbProvider.Kingbase => "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123",
    DbProvider.MySql => "Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    DbProvider.PostgreSQL => "Host=localhost;Port=5432;Database=station_spike;Username=station;Password=Station@123",
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_alerts.db")}"
};

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(typeof(Alert));
await sp.GetRequiredService<IAuthSeeder>().EnsureAsync();

var alerts = sp.GetRequiredService<IAlertService>();
var authorization = sp.GetRequiredService<IAuthorizationService>();
var userService = sp.GetRequiredService<IUserService>();
var accounts = sp.GetRequiredService<IRepository<Account>>();

// ---------- 1. 权限映射：admin 可处理，操作员仅查看 ----------
try
{
    var adminAccount = await accounts.FirstAsync(a => a.UserName == "admin");
    var adminHandle = await authorization.HasPermissionAsync(adminAccount!.Id, "alert:handle");

    await userService.CreateUserAsync(new UserDto(null, "U001", "张三", 1), "zhangsan", "Test@123");
    var operatorRole = (await userService.GetRolesAsync()).First(r => r.Code == "operator");
    var zhangSan = (await userService.GetUsersAsync()).First(u => u.UserNo == "U001");
    await userService.AssignRolesAsync(zhangSan.Id!.Value, [operatorRole.Id!.Value]);

    var zhangsanAccount = await accounts.FirstAsync(a => a.UserName == "zhangsan");
    var opHandle = await authorization.HasPermissionAsync(zhangsanAccount!.Id, "alert:handle");
    var opView = await authorization.HasPermissionAsync(zhangsanAccount.Id, "alert:view");

    Pass("权限映射", adminHandle && !opHandle && opView,
        $"admin alert:handle={adminHandle}, 操作员 handle={opHandle}, view={opView}");
}
catch (Exception ex)
{
    Pass("权限映射", false, ex.Message);
}

// ---------- 2. 写入 + 列表/筛选 ----------
try
{
    await alerts.WriteAsync(new Alert { Type = AlertType.UnauthorizedAccess, Level = AlertLevel.Critical, Title = "非授权接入", Detail = "SIM-ROGUE 拒绝接入", Source = "SIM-ROGUE" });
    await alerts.WriteAsync(new Alert { Type = AlertType.BindingInvalid, Level = AlertLevel.Warning, Title = "绑定文件疑似篡改", Detail = "SIM-R1 SM3 校验失败", Source = "SIM-R1" });
    await alerts.WriteAsync(new Alert { Type = AlertType.ChecksumFailed, Level = AlertLevel.Info, Title = "文件校验失败", Detail = "video_1.mp4", Source = "SIM-R1" });

    var all = await alerts.GetAlertsAsync(null, null, 10);
    var warningOnly = await alerts.GetAlertsAsync(AlertLevel.Warning, null, 10);
    var pendingOnly = await alerts.GetAlertsAsync(null, AlertStatus.Pending, 10);
    var pending = await alerts.CountPendingAsync();

    Pass("写入+筛选", all.Count == 3 && warningOnly.Count == 1 && pendingOnly.Count == 3 && pending == 3,
        $"全部={all.Count}, 警告={warningOnly.Count}, 待处理={pendingOnly.Count}, 待处理计数={pending}");
}
catch (Exception ex)
{
    Pass("写入+筛选", false, ex.Message);
}

// ---------- 3. 状态流转：确认/处理/关闭 ----------
try
{
    var all = await alerts.GetAlertsAsync(null, null, 10);
    var critical = all.First(a => a.Level == AlertLevel.Critical);
    var warning = all.First(a => a.Level == AlertLevel.Warning);
    var info = all.First(a => a.Level == AlertLevel.Info);

    var c1 = await alerts.SetStatusAsync(critical.Id, AlertStatus.Confirmed);
    var pendingAfterConfirm = await alerts.CountPendingAsync();
    var c2 = await alerts.SetStatusAsync(warning.Id, AlertStatus.Processed);
    var processed = await alerts.GetAlertsAsync(null, AlertStatus.Processed, 10);
    var c3 = await alerts.SetStatusAsync(info.Id, AlertStatus.Closed);
    var closed = await alerts.GetAlertsAsync(null, AlertStatus.Closed, 10);
    var pendingFinal = await alerts.CountPendingAsync();

    Pass("状态流转", c1 && c2 && c3 && pendingAfterConfirm == 2 && processed.Count == 1 && closed.Count == 1 && pendingFinal == 0,
        $"确认后待处理={pendingAfterConfirm}, 已处理={processed.Count}, 已关闭={closed.Count}, 最终待处理={pendingFinal}");
}
catch (Exception ex)
{
    Pass("状态流转", false, ex.Message);
}

// ---------- 4. 清理 ----------
try
{
    var db = sp.GetRequiredService<ISqlSugarClient>();
    db.DbMaintenance.DropTable<Alert>();
    db.DbMaintenance.DropTable<UserRole>();
    db.DbMaintenance.DropTable<RolePermission>();
    db.DbMaintenance.DropTable<Permission>();
    db.DbMaintenance.DropTable<Role>();
    db.DbMaintenance.DropTable<Account>();
    db.DbMaintenance.DropTable<User>();
    db.DbMaintenance.DropTable<Dept>();
    Pass("清理", true, "已删除测试表");
}
catch (Exception ex)
{
    Pass("清理", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 报警中心验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
