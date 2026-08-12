using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Collecting;
using Station.Application.Recorders;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Recorders;

// ---------------------------------------------------------------------------
// M9 Spike: 记录仪接入识别与归属（ini 绑定 + 三层识别 + 报警 + 采集归属继承）
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
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_recorder.db")}"
};

var testRoot = @"E:\Reny\station\archive\recorder-test";
var simDir = Path.Combine(testRoot, "sim");

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
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Collect:CacheDirectory"] = Path.Combine(testRoot, "cache"),
        ["Station:Collect:SimulatedSourceDirectory"] = simDir,
        ["Station:Collect:AutoCollectOnConnect"] = "true",
        ["Station:Collect:SimulatedFileCount"] = "3",
        ["Station:Collect:SimulatedChunkDelayMs"] = "5"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

var initializer = sp.GetRequiredService<IDatabaseInitializer>();
initializer.EnsureCreated(
    typeof(Recorder), typeof(Alert), typeof(CollectTask), typeof(CollectFile));
await sp.GetRequiredService<IAuthSeeder>().EnsureAsync();

var recorders = sp.GetRequiredService<IRecorderService>();
var identify = sp.GetRequiredService<IRecorderIdentificationService>();
var collect = sp.GetRequiredService<ICollectTaskService>();
var bindingFile = sp.GetRequiredService<RecorderBindingFile>();
var alerts = sp.GetRequiredService<IRepository<Alert>>();

async Task WaitTerminalAsync(long taskId, int timeoutSeconds = 60)
{
    var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
    while (DateTime.Now < deadline)
    {
        var task = await collect.GetTaskAsync(taskId);
        if (task is not null && task.Status is
            CollectTaskStatus.Completed or CollectTaskStatus.Interrupted
            or CollectTaskStatus.Failed or CollectTaskStatus.Canceled)
        {
            return;
        }

        await Task.Delay(150);
    }

    throw new TimeoutException("采集任务未按时结束");
}

var adminUserId = 1L; // 种子：系统管理员（UserNo=admin，根部门 1）
var rootDeptId = 1L;
var simRoot = simDir;
Directory.CreateDirectory(simDir);

// ---------- 1. 注册 + 写入绑定 ----------
try
{
    await recorders.RegisterAsync("SIM-R1", "模拟记录仪-X", ProtocolType.Ums, isAuthorized: true);
    await recorders.WriteBindingAsync("SIM-R1", adminUserId, rootDeptId, simRoot);
    var binding = bindingFile.Read(simRoot);
    var verified = binding is not null && bindingFile.VerifySignature(binding);
    var inWhitelist = (await recorders.GetRecordersAsync()).Any(r => r.SerialNumber == "SIM-R1" && r.IsAuthorized);
    Pass("注册+写绑定", verified && inWhitelist,
        $"ini 存在且签名有效={verified}, 台账白名单={inWhitelist}, 绑定={binding?.UserName}({binding?.DeptName})");
}
catch (Exception ex)
{
    Pass("注册+写绑定", false, ex.Message);
}

// ---------- 2. 识别：已绑定 → 归属继承 ----------
try
{
    var device = new CollectDeviceInfo("记录仪-R1", "SIM-R1", ProtocolType.Ums);
    var result = await identify.IdentifyAsync(device, simRoot);
    var ok = result.Status == RecorderIdentifyStatus.Bound
             && result.UserId == adminUserId
             && result.DeptId == rootDeptId;

    var task = await collect.CreateTaskAsync(device with { UserId = result.UserId, DeptId = result.DeptId }, isAuto: true);
    await WaitTerminalAsync(task.TaskId);
    var taskOk = task.OperatorUserId == adminUserId && task.DeptId == rootDeptId;
    Pass("识别+归属继承", ok && taskOk, $"识别={result.Status}, 用户={result.UserId}, 部门={result.DeptId}, 任务操作人继承={taskOk}, {result.Message}");
}
catch (Exception ex)
{
    Pass("识别+归属继承", false, ex.Message);
}

// ---------- 3. 篡改 ini → 疑似篡改 + 报警 ----------
try
{
    var path = Path.Combine(simRoot, RecorderBindingFile.FileName);
    var content = File.ReadAllText(path, Encoding.UTF8).Replace("user_name=系统管理员", "user_name=李四");
    File.WriteAllText(path, content, Encoding.UTF8);

    var result = await identify.IdentifyAsync(new CollectDeviceInfo("记录仪-R1", "SIM-R1", ProtocolType.Ums), simRoot);
    var alertCount = await alerts.CountAsync(a => a.Type == AlertType.BindingInvalid);
    Pass("篡改检测", result.Status == RecorderIdentifyStatus.InvalidSignature && alertCount >= 1,
        $"识别={result.Status}, BindingInvalid 报警数={alertCount}");
}
catch (Exception ex)
{
    Pass("篡改检测", false, ex.Message);
}

// ---------- 4. 无绑定 → 拒绝 + 报警 ----------
try
{
    bindingFile.Delete(simRoot);
    var result = await identify.IdentifyAsync(new CollectDeviceInfo("记录仪-R1", "SIM-R1", ProtocolType.Ums), simRoot);
    var alertCount = await alerts.CountAsync(a => a.Type == AlertType.BindingInvalid);
    Pass("未绑定拒绝", result.Status == RecorderIdentifyStatus.NoBinding && alertCount >= 2,
        $"识别={result.Status}, BindingInvalid 报警数={alertCount}");
}
catch (Exception ex)
{
    Pass("未绑定拒绝", false, ex.Message);
}

// ---------- 5. 非授权接入（编号不在台账） ----------
try
{
    var rogue = new BindingInfo(
        "SIM-ROGUE", "未知型号", "admin", "系统管理员", "ROOT", "总部", DateTime.Now, string.Empty);
    var sig = bindingFile.ComputeSignature(
        rogue.RecorderSerial, rogue.RecorderModel, rogue.UserNo, rogue.UserName, rogue.DeptCode, rogue.DeptName, rogue.BoundAt);
    bindingFile.Write(simRoot, rogue with { Signature = sig });

    var result = await identify.IdentifyAsync(new CollectDeviceInfo("记录仪-ROGUE", "SIM-ROGUE", ProtocolType.Ums), simRoot);
    var alertCount = await alerts.CountAsync(a => a.Type == AlertType.UnauthorizedAccess);
    Pass("非授权接入", result.Status == RecorderIdentifyStatus.UnknownRecorder && alertCount >= 1,
        $"识别={result.Status}, UnauthorizedAccess 报警数={alertCount}");
}
catch (Exception ex)
{
    Pass("非授权接入", false, ex.Message);
}

// ---------- 6. 清理 ----------
try
{
    var db = sp.GetRequiredService<ISqlSugarClient>();
    db.DbMaintenance.DropTable<Alert>();
    db.DbMaintenance.DropTable<Recorder>();
    db.DbMaintenance.DropTable<CollectFile>();
    db.DbMaintenance.DropTable<CollectTask>();
    db.DbMaintenance.DropTable<AuditLog>();
    db.DbMaintenance.DropTable<UserRole>();
    db.DbMaintenance.DropTable<RolePermission>();
    db.DbMaintenance.DropTable<Permission>();
    db.DbMaintenance.DropTable<Role>();
    db.DbMaintenance.DropTable<User>();
    db.DbMaintenance.DropTable<Dept>();
    db.DbMaintenance.DropTable<Account>();
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, true);
    }

    Pass("清理", true, "已删除测试表与测试目录");
}
catch (Exception ex)
{
    Pass("清理", false, ex.Message);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 记录仪识别验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
