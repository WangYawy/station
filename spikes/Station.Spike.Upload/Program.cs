using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SqlSugar;
using Station.Application;
using Station.Application.Collecting;
using Station.Application.Uploading;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Storage;

// ---------------------------------------------------------------------------
// M8 Spike: 上传/同步（本地磁盘 / FTP / SFTP）+ 熔断 + 失败重试
// ---------------------------------------------------------------------------

var dbArg = "sqlite";
var targetArg = "local";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length) dbArg = args[i + 1];
    if (args[i] == "--target" && i + 1 < args.Length) targetArg = args[i + 1];
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
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_upload.db")}"
};
if (provider == DbProvider.Sqlite && File.Exists(Path.Combine(AppContext.BaseDirectory, "spike_upload.db")))
{
    File.Delete(Path.Combine(AppContext.BaseDirectory, "spike_upload.db"));
}

var testRoot = @"E:\Reny\station\archive\upload-test";
var cacheDir = Path.Combine(testRoot, "cache");
var simDir = Path.Combine(testRoot, "sim");
var localRoot = Path.Combine(testRoot, "remote");

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var storageTarget = targetArg.ToLowerInvariant() switch
{
    "ftp" => StorageTargetKind.Ftp,
    "sftp" => StorageTargetKind.Sftp,
    _ => StorageTargetKind.Local
};
var sftpRoot = $"/home/kingbase/spike-upload-{DateTime.Now:yyyyMMddHHmmss}";

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Station:Db:Provider"] = provider.ToString(),
        ["Station:Db:ConnectionString"] = connStr,
        ["Station:Collect:CacheDirectory"] = cacheDir,
        ["Station:Collect:SimulatedSourceDirectory"] = simDir,
        ["Station:Collect:AutoCollectOnConnect"] = "true",
        ["Station:Collect:SimulatedFileCount"] = "5",
        ["Station:Collect:EncryptCache"] = "false",
        ["Station:Storage:Target"] = storageTarget.ToString(),
        ["Station:Storage:LocalRoot"] = localRoot,
        ["Station:Storage:StationNo"] = "ST0001",
        ["Station:Storage:FtpHost"] = "localhost",
        ["Station:Storage:FtpPort"] = "21",
        ["Station:Storage:FtpUser"] = "kingbase",
        ["Station:Storage:FtpPassword"] = "Kingbase@123",
        ["Station:Storage:SftpHost"] = "localhost",
        ["Station:Storage:SftpPort"] = "2222",
        ["Station:Storage:SftpUser"] = "kingbase",
        ["Station:Storage:SftpPassword"] = "Kingbase@123",
        ["Station:Storage:SftpRoot"] = sftpRoot,
        ["Station:Storage:RetryCount"] = "2",
        ["Station:Storage:RetryIntervalSeconds"] = "0",
        ["Station:Storage:CircuitBreakerThreshold"] = "3",
        ["Station:Storage:CircuitBreakerCooldownSeconds"] = "2",
        ["Station:Storage:ChunkBytes"] = "1048576"
    })
    .Build();

var services = new ServiceCollection();
services.AddStationDatabase(config);
services.AddStationApplication(config);
await using var sp = services.BuildServiceProvider();

sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(
    typeof(CollectTask), typeof(CollectFile), typeof(LicenseInfo), typeof(Alert), typeof(AuditLog));
var collect = sp.GetRequiredService<ICollectTaskService>();
var upload = sp.GetRequiredService<IUploadService>();
var sim = (SimulatedCollectSource)sp.GetRequiredService<ICollectSource>();

async Task ResetSimAsync()
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

async Task WaitTerminalAsync(long taskId, int timeoutSeconds = 90)
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

    throw new TimeoutException("采集任务未按时完成");
}

async Task<UploadSummary> WaitUploadedAsync(long taskId, int timeoutSeconds = 90)
{
    var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
    while (DateTime.Now < deadline)
    {
        var summary = await upload.ProcessTaskAsync(taskId);
        if (summary.Pending == 0)
        {
            return summary;
        }

        await Task.Delay(200);
    }

    throw new TimeoutException("上传未按时完成");
}

// ---------- 1. 采集 → 上传（本地/FTP/SFTP 端到端） ----------
try
{
    await ResetSimAsync();
    var task = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-UP", "SIM-UP", ProtocolType.Ums), isAuto: true);
    await WaitTerminalAsync(task.TaskId);
    var summary = await WaitUploadedAsync(task.TaskId);
    var files = await collect.GetTaskFilesAsync(task.TaskId);
    var allUploaded = files.Count == 5 && files.All(f => f.Status == CollectFileStatus.Completed && f.RemotePath is not null);
    var firstError = files.FirstOrDefault(f => f.RemotePath is null)?.UploadError;

    var remoteVerified = true;
    if (storageTarget == StorageTargetKind.Local)
    {
        var target = sp.GetRequiredService<IStorageTarget>();
        foreach (var file in files)
        {
            var size = await target.GetRemoteSizeAsync(file.RemotePath!, CancellationToken.None);
            if (size != file.Size)
            {
                remoteVerified = false;
            }
        }
    }

    var firstRemote = files.FirstOrDefault()?.RemotePath;
    var templateOk = firstRemote is not null
                     && firstRemote.Contains("ST0001")
                     && firstRemote.Contains(DateTime.Today.ToString("yyyy-MM-dd"));
    Pass("采集→上传", summary.Uploaded == 5 && allUploaded && remoteVerified && templateOk,
        $"目标={storageTarget}, 上传={summary.Uploaded}/5, 远端校验={remoteVerified}, 模板示例={firstRemote}, 错误={firstError}");
}
catch (Exception ex)
{
    Pass("采集→上传", false, ex.ToString());
}

// ---------- 2. 熔断：连续失败打开 → 冷却后半开探测成功关闭 ----------
try
{
    await ResetSimAsync();
    var breakerTask = await collect.CreateTaskAsync(new CollectDeviceInfo("记录仪-BK", "SIM-BK", ProtocolType.Ums), isAuto: true);
    await WaitTerminalAsync(breakerTask.TaskId);

    var factory = sp.GetRequiredService<ISqlSugarFactory>();
    var dbOptions = sp.GetRequiredService<DbOptions>();
    var storageOptions = sp.GetRequiredService<StorageOptions>();
    var localTarget = new LocalDiskStorageTarget(Options.Create(storageOptions));
    var flaky = new FailingStorageTarget(localTarget, failCount: 99);
    var breaker = new StorageCircuitBreaker(3, 2);
    var breakerUpload = new UploadService(
        sp.GetRequiredService<IRepository<CollectTask>>(),
        sp.GetRequiredService<IRepository<CollectFile>>(),
        flaky,
        storageOptions,
        sp.GetRequiredService<CollectOptions>(),
        breaker,
        factory,
        dbOptions);

    for (var i = 0; i < 4; i++)
    {
        await breakerUpload.ProcessTaskAsync(breakerTask.TaskId);
        if (breaker.IsOpen)
        {
            break;
        }
    }

    var opened = breaker.IsOpen;
    var stuckOpen = breaker.IsOpen; // 冷却期内应立即仍为打开

    await Task.Delay(2500); // 冷却结束
    flaky.SetHealthy();
    var recovered = await breakerUpload.ProcessTaskAsync(breakerTask.TaskId);
    var closed = !breaker.IsOpen;
    var allUp = recovered.Uploaded == 5;
    Pass("熔断+恢复", opened && stuckOpen && closed && allUp,
        $"打开={opened}, 冷却期内保持打开={stuckOpen}, 恢复后关闭={closed}, 恢复后上传={recovered.Uploaded}/5");
}
catch (Exception ex)
{
    Pass("熔断+恢复", false, ex.Message);
}

// ---------- 3. 清理 ----------
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
    Pass("清理", false, ex.Message);
}

// 清理 SFTP 远端测试目录（含历史遗留），保持服务器整洁
if (storageTarget == StorageTargetKind.Sftp)
{
    try
    {
        using var sftp = new SftpClient("localhost", 2222, "kingbase", "Kingbase@123");
        sftp.Connect();
        foreach (var entry in sftp.ListDirectory("/home/kingbase"))
        {
            if (entry.IsDirectory && entry.Name.StartsWith("spike-upload-"))
            {
                DeleteRemoteRecursive(sftp, $"/home/kingbase/{entry.Name}");
            }
        }

        if (sftp.Exists("/home/kingbase/ST0001"))
        {
            DeleteRemoteRecursive(sftp, "/home/kingbase/ST0001");
        }

        sftp.Disconnect();
    }
    catch
    {
        // 远端清理失败不影响结论
    }
}

static void DeleteRemoteRecursive(SftpClient sftp, string path)
{
    foreach (var entry in sftp.ListDirectory(path))
    {
        if (entry.Name is "." or "..")
        {
            continue;
        }

        var full = $"{path.TrimEnd('/')}/{entry.Name}";
        if (entry.IsDirectory)
        {
            DeleteRemoteRecursive(sftp, full);
        }
        else
        {
            sftp.DeleteFile(full);
        }
    }

    sftp.DeleteDirectory(path);
}

Console.WriteLine();
Console.WriteLine($"================ {provider}/{storageTarget} 上传验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;

internal sealed class FailingStorageTarget : IStorageTarget
{
    private readonly IStorageTarget _inner;
    private int _remainingFailures;

    public FailingStorageTarget(IStorageTarget inner, int failCount)
    {
        _inner = inner;
        _remainingFailures = failCount;
    }

    public string Name => "flaky";

    public Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken cancellationToken)
    {
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new IOException("模拟存储故障");
        }

        return _inner.UploadAsync(file, onProgress, cancellationToken);
    }

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken) =>
        _inner.GetRemoteSizeAsync(remotePath, cancellationToken);

    public void SetHealthy() => _remainingFailures = 0;
}
