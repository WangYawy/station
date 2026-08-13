using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Contracts;
using Station.Desktop.Bootstrapper;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Security;
using Station.Infrastructure.Storage;

// ---------------------------------------------------------------------------
// M42 Spike: 定时采集 / 暂停恢复 / 缓存清理 / 远端SM3 / 时钟回拨
// ---------------------------------------------------------------------------

const int WebPort = 5129;
Environment.CurrentDirectory = @"E:\Reny\station\src\Station.Desktop\Station.Desktop.WebHost";
var testRoot = Path.Combine(Path.GetTempPath(), "station-m42-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var dbFile = Path.Combine(testRoot, "station.db");
var cacheDir = Path.Combine(testRoot, "cache");
var simDir = Path.Combine(testRoot, "sim");
var remoteRoot = Path.Combine(testRoot, "remote");
var scheduleMinute = (DateTime.Now.Minute + 1) % 60;

Environment.SetEnvironmentVariable("STATION__DB__PROVIDER", "Sqlite");
Environment.SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", $"Data Source={dbFile}");
Environment.SetEnvironmentVariable("STATION__COLLECT__CACHEDIRECTORY", cacheDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDSOURCEDIRECTORY", simDir);
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDFILECOUNT", "2");
Environment.SetEnvironmentVariable("STATION__COLLECT__SIMULATEDCHUNKDELAYMS", "200");
Environment.SetEnvironmentVariable("STATION__COLLECT__ENCRYPTCACHE", "false");
Environment.SetEnvironmentVariable("STATION__COLLECT__SKIPCOLLECTED", "false");
Environment.SetEnvironmentVariable("STATION__COLLECT__SCHEDULEDCOLLECTENABLED", "true");
Environment.SetEnvironmentVariable("STATION__COLLECT__SCHEDULEHOUR", DateTime.Now.Hour.ToString());
Environment.SetEnvironmentVariable("STATION__COLLECT__SCHEDULEMINUTE", scheduleMinute.ToString());
Environment.SetEnvironmentVariable("STATION__STORAGE__TARGET", "Local");
Environment.SetEnvironmentVariable("STATION__STORAGE__LOCALROOT", remoteRoot);
Environment.SetEnvironmentVariable("STATION__STORAGE__STATIONNO", "ST0001");
Environment.SetEnvironmentVariable("STATION__AUTH__AUTOLOGOUTMINUTES", "0");
Environment.SetEnvironmentVariable("STATION__WEB__PORT", WebPort.ToString());
Environment.SetEnvironmentVariable("STATION__WEB__ENABLELAN", "false");

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

var collect = host.Services.GetRequiredService<ICollectTaskService>();
var db = host.Services.GetRequiredService<ISqlSugarClient>();
var collectOptions = host.Services.GetRequiredService<CollectOptions>();
Console.WriteLine($"[DIAG] 采集配置: delay={collectOptions.SimulatedChunkDelayMs}, count={collectOptions.SimulatedFileCount}, encrypt={collectOptions.EncryptCache}");

// ---------- 1. 定时采集 ----------
try
{
    var scheduled = false;
    var wait = DateTime.Now.AddSeconds(110);
    while (DateTime.Now < wait)
    {
        var task = db.Queryable<CollectTask>().Where(t => t.RecorderName == "定时采集").First();
        if (task is not null && task.Status == CollectTaskStatus.Completed)
        {
            scheduled = true;
            break;
        }

        await Task.Delay(1000);
    }

    Pass("定时采集", scheduled, $"计划={DateTime.Now.Hour}:{scheduleMinute}, 任务状态={db.Queryable<CollectTask>().Where(t => t.RecorderName == "定时采集").First()?.Status}");
}
catch (Exception ex)
{
    Pass("定时采集", false, ex.ToString());
}

// ---------- 2. 暂停/恢复 ----------
try
{
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("暂停恢复", "SIM-PAUSE", Station.Contracts.ProtocolType.Ums), isAuto: true);
    await Task.Delay(250);
    await collect.PauseAsync(task.TaskId);
    var pausedDeadline = DateTime.Now.AddSeconds(10);
    CollectTaskStatus paused = CollectTaskStatus.Collecting;
    while (DateTime.Now < pausedDeadline)
    {
        paused = (await collect.GetTaskAsync(task.TaskId))!.Status;
        Console.WriteLine($"[DIAG] 暂停轮询: {paused}");
        if (paused == CollectTaskStatus.Paused)
        {
            break;
        }

        await Task.Delay(200);
    }

    await collect.ResumeAsync(task.TaskId);
    var terminal = await WaitTerminalAsync(collect, task.TaskId, 60);
    Pass("暂停/恢复", paused == CollectTaskStatus.Paused && terminal == CollectTaskStatus.Completed,
        $"暂停后状态={paused}, 恢复后状态={terminal}");
}
catch (Exception ex)
{
    Pass("暂停/恢复", false, ex.ToString());
}

// ---------- 3. 缓存保留天数清理 ----------
try
{
    Directory.CreateDirectory(cacheDir);
    var oldFile = Path.Combine(cacheDir, "old-cache.bin");
    File.WriteAllText(oldFile, "old");
    File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-40));
    var cleanup = host.Services.GetRequiredService<ICacheCleanupService>();
    var (count, _) = await cleanup.CleanupAsync();
    Pass("缓存保留天数清理", count == 1 && !File.Exists(oldFile), $"清理 {count} 个超期文件");
}
catch (Exception ex)
{
    Pass("缓存保留天数清理", false, ex.ToString());
}

// ---------- 4. 远端 SM3 二次校验（本地目标 + SFTP） ----------
try
{
    var manual = await collect.CreateTaskAsync(new CollectDeviceInfo("SM3校验", "SIM-SM3", Station.Contracts.ProtocolType.Ums), isAuto: true);
    await WaitTerminalAsync(collect, manual.TaskId, 60);
    Console.WriteLine("[DIAG] SM3任务终态");
    var ledger = host.Services.GetRequiredService<IFileLedgerService>();
    await ledger.ProcessCompletedTaskAsync(manual.TaskId);
    Console.WriteLine("[DIAG] 台账完成");
    var upload = host.Services.GetRequiredService<Station.Application.Uploading.IUploadService>();
    var summary = await upload.ProcessTaskAsync(manual.TaskId);
    Console.WriteLine("[DIAG] 上传完成");
    var files = db.Queryable<CollectFile>().Where(f => f.TaskId == manual.TaskId).ToList();
    Console.WriteLine($"[DIAG] SM3任务文件数={files.Count}, 上传={summary.Uploaded}, remote={files.FirstOrDefault(f => f.Extension == ".mp4")?.RemotePath}");
    var video = files.First(f => f.Extension == ".mp4");
    var localTarget = host.Services.GetRequiredService<IStorageTarget>();
    var remoteSm3 = await localTarget.ComputeRemoteSm3Async(video.RemotePath!, CancellationToken.None);
    var sourceSm3 = Sm3Checksum.ComputeFile(Path.Combine(simDir, video.FileName));

    var sftpStorage = new SftpStorageTarget(Microsoft.Extensions.Options.Options.Create(new StorageOptions
    {
        Target = StorageTargetKind.Sftp,
        SftpHost = "127.0.0.1",
        SftpPort = 2222,
        SftpUser = "kingbase",
        SftpPassword = "Kingbase@123",
        ChunkBytes = 64 * 1024
    }));
    var sftpLocal = Path.Combine(testRoot, "sftp-local.bin");
    File.WriteAllBytes(sftpLocal, new byte[4096]);
    var sftpRemote = $"station-m42/{DateTime.Now:yyyyMMddHHmmss}/f.bin";
    await sftpStorage.UploadAsync(new UploadTargetFile(sftpLocal, sftpRemote, 4096), null, CancellationToken.None);
    var sftpRemoteSm3 = await sftpStorage.ComputeRemoteSm3Async(sftpRemote, CancellationToken.None);
    var sftpLocalSm3 = Sm3Checksum.ComputeFile(sftpLocal);
    using var cleanupSftp = new Renci.SshNet.SftpClient("127.0.0.1", 2222, "kingbase", "Kingbase@123");
    cleanupSftp.Connect();
    cleanupSftp.DeleteFile(sftpRemote);
    cleanupSftp.DeleteDirectory(sftpRemote[..sftpRemote.LastIndexOf('/')]);
    cleanupSftp.DeleteDirectory("station-m42");
    cleanupSftp.Disconnect();
    File.Delete(sftpLocal);

    Pass("远端SM3二次校验", video.RemotePath is not null && remoteSm3 == sourceSm3 && sftpRemoteSm3 == sftpLocalSm3,
        $"本地目标={remoteSm3 == sourceSm3}, SFTP={sftpRemoteSm3 == sftpLocalSm3}, RemotePath={(video.RemotePath is null ? "null" : "ok")}");
}
catch (Exception ex)
{
    Pass("远端SM3二次校验", false, ex.ToString());
}

// ---------- 5. 时钟回拨检测 ----------
try
{
    var license = host.Services.GetRequiredService<ILicenseService>();
    var state = db.Queryable<ClockState>().First(c => c.Id == 1);
    state.LastCheckAt = DateTime.Now.AddHours(1);
    db.Updateable(state).ExecuteCommand();
    var locked = await license.CheckAsync();
    state.LastCheckAt = DateTime.Now;
    db.Updateable(state).ExecuteCommand();
    var normal = await license.CheckAsync();
    Pass("时钟回拨检测", locked.Status == LicenseStatus.Locked && normal.Status != LicenseStatus.Locked,
        $"回拨→{locked.Status}({locked.Message}), 恢复→{normal.Status}");
}
catch (Exception ex)
{
    Pass("时钟回拨检测", false, ex.ToString());
}

await host.StopAsync();
host.Dispose();
try
{
    Directory.Delete(testRoot, true);
}
catch
{
}

Console.WriteLine();
Console.WriteLine("================ M42 采集运维能力 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

static async Task<CollectTaskStatus> WaitTerminalAsync(ICollectTaskService collect, long taskId, int seconds)
{
    var deadline = DateTime.Now.AddSeconds(seconds);
    while (DateTime.Now < deadline)
    {
        var task = await collect.GetTaskAsync(taskId);
        if (task is not null && task.Status is
            CollectTaskStatus.Completed or CollectTaskStatus.Failed or CollectTaskStatus.Interrupted or CollectTaskStatus.Canceled)
        {
            return task.Status;
        }

        await Task.Delay(300);
    }

    throw new TimeoutException("任务未按时到达终态");
}
