using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;

// ---------------------------------------------------------------------------
// M7 Spike: 采集作业流程（模拟记录仪）
// 自动采集 / 跳过已采集 / 中断(拔线) / 暂停恢复 / 取消 / 完成后擦除
// ---------------------------------------------------------------------------

var dbArg = "";
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
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_collect.db")}"
};

var testRoot = @"E:\Reny\station\archive\collect-test";
var cacheDir = Path.Combine(testRoot, "cache");
var simDir = Path.Combine(testRoot, "sim");

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}
void Fail(string step, Exception ex) { results.Add((step, false, ex.Message)); Console.WriteLine($"[FAIL] {step}: {ex.Message}"); }

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Collect:CacheDirectory"] = cacheDir,
        ["Station:Collect:SimulatedSourceDirectory"] = simDir,
        ["Station:Collect:AutoCollectOnConnect"] = "true",
        ["Station:Collect:SkipCollected"] = "true",
        ["Station:Collect:EraseAfterComplete"] = "false",
        ["Station:Collect:ChunkBytes"] = "1048576",
        ["Station:Collect:SimulatedChunkDelayMs"] = "15",
        ["Station:Collect:SimulatedFileCount"] = "5"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(typeof(CollectTask), typeof(CollectFile));

var collect = sp.GetRequiredService<ICollectTaskService>();
var sim = (SimulatedCollectSource)sp.GetRequiredService<ICollectSource>();

async Task ResetSimFilesAsync()
{
    if (Directory.Exists(simDir))
    {
        foreach (var f in Directory.GetFiles(simDir))
        {
            File.Delete(f);
        }
    }

    await sim.ScanAsync(new CollectDeviceInfo("重置", null, ProtocolType.Ums), CancellationToken.None);
}

async Task<CollectTaskDto> WaitTerminalAsync(long taskId, int timeoutSeconds = 60)
{
    var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
    while (DateTime.Now < deadline)
    {
        var task = await collect.GetTaskAsync(taskId);
        if (task is not null && task.Status is
            CollectTaskStatus.Completed or CollectTaskStatus.Interrupted
            or CollectTaskStatus.Failed or CollectTaskStatus.Canceled)
        {
            return task;
        }

        await Task.Delay(150);
    }

    throw new TimeoutException($"任务 {taskId} 未在 {timeoutSeconds}s 内结束");
}

// ---------- 1. 自动采集（接入即采） ----------
try
{
    await ResetSimFilesAsync();
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-A", "SIM-A", ProtocolType.Ums), isAuto: true);
    var done = await WaitTerminalAsync(task.TaskId);
    var files = await collect.GetTaskFilesAsync(task.TaskId);
    var ok = done.Status == CollectTaskStatus.Completed
             && done.CollectedFiles == 5
             && done.SkippedFiles == 0
             && done.CollectedBytes == done.TotalBytes
             && files.All(f => f.Status == CollectFileStatus.Completed);
    Pass("自动采集", ok, $"状态={done.Status}, 采集={done.CollectedFiles}/5, 跳过={done.SkippedFiles}, 字节一致={done.CollectedBytes == done.TotalBytes}");
}
catch (Exception ex)
{
    Fail("自动采集", ex);
}

// ---------- 2. 已采集文件自动跳过 ----------
try
{
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-B", "SIM-B", ProtocolType.Ums), isAuto: true);
    var done = await WaitTerminalAsync(task.TaskId);
    var ok = done.Status == CollectTaskStatus.Completed
             && done.SkippedFiles == 5
             && done.CollectedFiles == 0;
    Pass("跳过已采集", ok, $"状态={done.Status}, 跳过={done.SkippedFiles}/5, 新采集={done.CollectedFiles}");
}
catch (Exception ex)
{
    Fail("跳过已采集", ex);
}

// ---------- 3. 采集中途拔线（中断） ----------
try
{
    await ResetSimFilesAsync();
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-C", "SIM-C", ProtocolType.Ums), isAuto: false);
    await collect.StartAsync(task.TaskId);
    await Task.Delay(250);
    await collect.InterruptAsync(task.TaskId, "模拟拔出：设备断开");

    var done = await WaitTerminalAsync(task.TaskId);
    var files = await collect.GetTaskFilesAsync(task.TaskId);
    var hasAbnormal = files.Any(f => f.Status == CollectFileStatus.Abnormal);
    var hasCanceled = files.Any(f => f.Status == CollectFileStatus.Canceled);
    var ok = done.Status == CollectTaskStatus.Interrupted && (hasAbnormal || hasCanceled);
    Pass("中途拔线(中断)", ok, $"状态={done.Status}, 异常={files.Count(f => f.Status == CollectFileStatus.Abnormal)}, 取消={files.Count(f => f.Status == CollectFileStatus.Canceled)}, 完成={files.Count(f => f.Status == CollectFileStatus.Completed)}");
}
catch (Exception ex)
{
    Fail("中途拔线(中断)", ex);
}

// ---------- 4. 暂停 / 恢复 ----------
try
{
    await ResetSimFilesAsync();
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-D", "SIM-D", ProtocolType.Ums), isAuto: false);
    await collect.StartAsync(task.TaskId);
    await Task.Delay(150);
    await collect.PauseAsync(task.TaskId);

    CollectTaskDto paused;
    var deadline = DateTime.Now.AddSeconds(10);
    do
    {
        await Task.Delay(100);
        paused = (await collect.GetTaskAsync(task.TaskId))!;
    } while (paused.Status != CollectTaskStatus.Paused && DateTime.Now < deadline);

    await collect.ResumeAsync(task.TaskId);
    var done = await WaitTerminalAsync(task.TaskId);
    var ok = paused.Status == CollectTaskStatus.Paused
             && done.Status == CollectTaskStatus.Completed
             && done.CollectedFiles == 5;
    Pass("暂停/恢复", ok, $"暂停状态={paused.Status}, 最终={done.Status}, 采集={done.CollectedFiles}/5");
}
catch (Exception ex)
{
    Fail("暂停/恢复", ex);
}

// ---------- 5. 取消 ----------
try
{
    await ResetSimFilesAsync();
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-E", "SIM-E", ProtocolType.Ums), isAuto: false);
    await collect.StartAsync(task.TaskId);
    await Task.Delay(120);
    await collect.CancelAsync(task.TaskId);
    var done = await WaitTerminalAsync(task.TaskId);
    var ok = done.Status == CollectTaskStatus.Canceled;
    Pass("取消", ok, $"状态={done.Status}");
}
catch (Exception ex)
{
    Fail("取消", ex);
}

// ---------- 6. 完成后擦除（开启擦除策略） ----------
try
{
    await ResetSimFilesAsync();
    var eraseConfig = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Station:Db:Provider"] = provider.ToString(),
            ["Station:Db:ConnectionString"] = connStr,
            ["Station:Collect:CacheDirectory"] = cacheDir,
            ["Station:Collect:SimulatedSourceDirectory"] = simDir,
            ["Station:Collect:AutoCollectOnConnect"] = "true",
            ["Station:Collect:EraseAfterComplete"] = "true",
            ["Station:Collect:SimulatedChunkDelayMs"] = "15",
            ["Station:Collect:SimulatedFileCount"] = "5"
        })
        .Build();
    var eraseServices = new ServiceCollection();
    eraseServices.AddStationDatabase(eraseConfig);
    eraseServices.AddStationApplication(eraseConfig);
    await using var eraseSp = eraseServices.BuildServiceProvider();
    eraseSp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(typeof(CollectTask), typeof(CollectFile));
    var eraseCollect = eraseSp.GetRequiredService<ICollectTaskService>();

    var task = await eraseCollect.CreateTaskAsync(new CollectDeviceInfo("记录仪-F", "SIM-F", ProtocolType.Ums), isAuto: true);
    var done = await WaitTerminalAsync(task.TaskId);
    var remaining = Directory.Exists(simDir) ? Directory.GetFiles(simDir).Length : 0;
    var ok = done.Status == CollectTaskStatus.Completed && remaining == 0;
    Pass("完成后擦除", ok, $"状态={done.Status}, 源目录剩余文件={remaining}");
}
catch (Exception ex)
{
    Fail("完成后擦除", ex);
}

// ---------- 7. 清理 ----------
try
{
    var db = sp.GetRequiredService<ISqlSugarClient>();
    db.DbMaintenance.DropTable<CollectFile>();
    db.DbMaintenance.DropTable<CollectTask>();
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, true);
    }

    Pass("清理", true, "已删除测试表与测试目录");
}
catch (Exception ex)
{
    Fail("清理", ex);
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 采集作业验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
