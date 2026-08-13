using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Application;
using Station.Application.Collecting;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Registration;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using ProtocolType = Station.Contracts.ProtocolType;

// ---------------------------------------------------------------------------
// M12 Spike: 端到端 采集→SM3/台账→平台元数据上报（真实平台 API + MySQL 持久化）
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
    _ => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_platforme2e.db")}"
};

const int PlatformPort = 5101;
var testRoot = @"E:\Reny\station\archive\platform-e2e";
var simDir = Path.Combine(testRoot, "sim");

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// 启动真实平台 API（MySQL station_platform 持久化）
var platformExe = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api\bin\Release\net8.0\Station.Platform.Api.exe";
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = Path.GetDirectoryName(platformExe)!,
    UseShellExecute = false,
    CreateNoWindow = true
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

try
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Station:Db:Provider"] = provider.ToString(),
            ["Station:Db:ConnectionString"] = connStr,
            ["Station:Collect:CacheDirectory"] = Path.Combine(testRoot, "cache"),
            ["Station:Collect:SimulatedSourceDirectory"] = simDir,
            ["Station:Collect:AutoCollectOnConnect"] = "true",
            ["Station:Collect:SimulatedFileCount"] = "3",
            ["Station:Collect:SimulatedChunkDelayMs"] = "5",
            ["Station:Platform:Enabled"] = "true",
            ["Station:Platform:BaseUrl"] = $"http://127.0.0.1:{PlatformPort}",
            ["Station:Platform:StationCode"] = "ST-E2E"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddStationDatabase(config);
    services.AddStationApplication(config);
    await using var sp = services.BuildServiceProvider();

    sp.GetRequiredService<IDatabaseInitializer>().EnsureCreated(
        typeof(CollectTask), typeof(CollectFile), typeof(VideoFile), typeof(SyncOutbox));
    await sp.GetRequiredService<IAuthSeeder>().EnsureAsync();

    var collect = sp.GetRequiredService<ICollectTaskService>();
    var ledger = sp.GetRequiredService<IFileLedgerService>();
    var outbox = sp.GetRequiredService<ISyncOutboxService>();
    var client = sp.GetRequiredService<IPlatformClient>();
    var context = sp.GetRequiredService<IStationContext>();
    var videoFiles = sp.GetRequiredService<IRepository<VideoFile>>();

    // ---------- 1. 注册（真实平台 API） ----------
    try
    {
        var register = new StationRegistrationRequest
        {
            StationCode = "ST-E2E",
            MachineFingerprint = new MachineFingerprint
            {
                CpuSerial = "cpu-e2e",
                MotherboardSerial = "mb-e2e",
                DiskSerial = "disk-e2e",
                MacAddress = "00:11:22:33:44:55"
            },
            OsVersion = "win11",
            CpuArch = "x86_64",
            SoftwareVersion = "0.1.0"
        };
        var response = await client.RegisterAsync(register, CancellationToken.None);
        context.Set(response!.StationId);
        Pass("平台注册", response.StationId > 0,
            $"StationId={response.StationId}, StationCode={response.StationCode}");
    }
    catch (Exception ex)
    {
        Pass("平台注册", false, ex.Message);
    }

    // ---------- 2. 采集完成 ----------
    long taskId = 0;
    try
    {
        var task = await collect.CreateTaskAsync(
            new CollectDeviceInfo("记录仪-E2E", "SIM-E2E", ProtocolType.Ums, UserId: 1, DeptId: 1),
            isAuto: true);
        taskId = task.TaskId;
        var deadline2 = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < deadline2)
        {
            var current = await collect.GetTaskAsync(taskId);
            if (current is not null && current.Status is
                CollectTaskStatus.Completed or CollectTaskStatus.Interrupted
                or CollectTaskStatus.Failed or CollectTaskStatus.Canceled)
            {
                break;
            }

            await Task.Delay(150);
        }

        var done = await collect.GetTaskAsync(taskId);
        Pass("采集完成", done is { Status: CollectTaskStatus.Completed, CollectedFiles: 3 },
            $"状态={done?.Status}, 采集={done?.CollectedFiles}/3");
    }
    catch (Exception ex)
    {
        Pass("采集完成", false, ex.Message);
    }

    // ---------- 3. 台账 + 元数据入队 ----------
    try
    {
        var ledgered = await ledger.ProcessCompletedTaskAsync(taskId);
        var pending = await outbox.CountPendingAsync();
        var ledgerCount = await videoFiles.CountAsync();
        var sample = (await videoFiles.GetListAsync()).FirstOrDefault();
        var sm3Ok = sample is { Sm3.Length: 64 } && sample.FileNo.StartsWith("ST-E2E-");
        Pass("台账+SM3+FileNo", ledgered == 3 && pending == 3 && ledgerCount == 3 && sm3Ok,
            $"入台账={ledgered}, 待上报={pending}, 台账数={ledgerCount}, SM3长度={sample?.Sm3?.Length}, FileNo={sample?.FileNo}");
    }
    catch (Exception ex)
    {
        Pass("台账+SM3+FileNo", false, ex.Message);
    }

    // ---------- 4. 上报 + 平台 MySQL 持久化 ----------
    try
    {
        var sent = await outbox.DrainAsync();
        var platformDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        var platformCount = platformDb.Queryable<Station.Platform.Domain.Entities.PlatformFileMetadata>().Count();
        var platformStation = platformDb.Queryable<Station.Platform.Domain.Entities.PlatformStation>()
            .Where(s => s.StationCode == "ST-E2E").Count();
        Pass("上报+平台落库", sent == 3 && platformCount >= 3 && platformStation == 1,
            $"上报={sent}, 平台文件元数据={platformCount}, 平台注册站={platformStation}");
        platformDb.Dispose();
    }
    catch (Exception ex)
    {
        Pass("上报+平台落库", false, ex.Message);
    }

    // ---------- 5. 清理采集站表 ----------
    try
    {
        var db = sp.GetRequiredService<ISqlSugarClient>();
        db.DbMaintenance.DropTable<SyncOutbox>();
        db.DbMaintenance.DropTable<VideoFile>();
        db.DbMaintenance.DropTable<CollectFile>();
        db.DbMaintenance.DropTable<CollectTask>();
        Pass("清理", true, "已删除采集站测试表");
    }
    catch (Exception ex)
    {
        Pass("清理", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine($"================ {provider} 平台端到端验证 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项");
return failed == 0 ? 0 : 1;
