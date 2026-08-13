using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Infrastructure.Backup;
using Station.Infrastructure.Db;

// ---------------------------------------------------------------------------
// M41 Spike: 数据备份（SQLite 快照 + 逻辑SQL导出跨库 + 保留裁剪 + 平台手动备份端点）
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

// ================= Phase A：SQLite 备份/还原/保留裁剪 =================
var rootA = Path.Combine(Path.GetTempPath(), "station-m41-sqlite-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(rootA);
var dbA = Path.Combine(rootA, "a.db");
var dbB = Path.Combine(rootA, "b.db");
var backupDirA = Path.Combine(rootA, "backups");

try
{
    using (var db = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={dbA}",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    }))
    {
        db.CodeFirst.InitTables(typeof(SpikeBackupEntity));
        for (var i = 1; i <= 3; i++)
        {
            db.Insertable(new SpikeBackupEntity { Id = i, Name = $"row-{i}" }).ExecuteCommand();
        }
    }

    var serviceA = new DatabaseBackupService(
        new DbOptions { Provider = DbProvider.Sqlite, ConnectionString = $"Data Source={dbA}" },
        Options.Create(new BackupOptions { BackupDirectory = backupDirA, RetentionCount = 3 }),
        new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={dbA}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true
        }));
    var backupPath = await serviceA.CreateBackupAsync();

    using (var dbBClient = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={dbB};Pooling=False",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    }))
    {
        dbBClient.CodeFirst.InitTables(typeof(SpikeBackupEntity));
    }

    var serviceB = new DatabaseBackupService(
        new DbOptions { Provider = DbProvider.Sqlite, ConnectionString = $"Data Source={dbB}" },
        Options.Create(new BackupOptions { BackupDirectory = backupDirA, RetentionCount = 3 }),
        new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={dbB}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true
        }));
    await serviceB.RestoreAsync(backupPath);
    using (var backupCheck = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={backupPath};Pooling=False",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    }))
    {
        Console.WriteLine($"[DIAG] 备份文件直读行数={backupCheck.Queryable<SpikeBackupEntity>().Count()}");
    }

    using (var check = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = $"Data Source={dbB};Pooling=False",
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = true
    }))
    {
        var count = check.Queryable<SpikeBackupEntity>().Count();
        Console.WriteLine($"[DIAG] dbB文件长度={new FileInfo(dbB).Length}, 行数={count}");
        Pass("SQLite备份与还原", File.Exists(backupPath) && count == 3, $"备份={Path.GetFileName(backupPath)}, 还原后行数={count}");
    }

    for (var i = 0; i < 5; i++)
    {
        await serviceA.CreateBackupAsync();
    }

    var list = serviceA.ListBackups();
    Pass("备份保留裁剪", list.Count == 3, $"保留 {list.Count} 份（配置 3）");
}
catch (Exception ex)
{
    Pass("SQLite备份", false, ex.ToString());
}

// ================= Phase B：MySQL 逻辑导出/还原（station_spike 幂等还原） =================
var rootB = Path.Combine(Path.GetTempPath(), "station-m41-mysql-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(rootB);

try
{
    var spikeConn = "Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None";
    using (var db = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = spikeConn,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true
    }))
    {
        db.CodeFirst.InitTables(typeof(SpikeBackupEntity));
        db.Deleteable<SpikeBackupEntity>().ExecuteCommand();
        for (var i = 1; i <= 3; i++)
        {
            db.Insertable(new SpikeBackupEntity { Id = i, Name = $"mysql-row-{i}" }).ExecuteCommand();
        }
    }

    var service = new DatabaseBackupService(
        new DbOptions { Provider = DbProvider.MySql, ConnectionString = spikeConn },
        Options.Create(new BackupOptions { BackupDirectory = rootB, RetentionCount = 30 }),
        new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = spikeConn,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }));
    var dump = await service.CreateBackupAsync();
    var dumpText = await File.ReadAllTextAsync(dump);

    var restoreService = new DatabaseBackupService(
        new DbOptions { Provider = DbProvider.MySql, ConnectionString = spikeConn },
        Options.Create(new BackupOptions { BackupDirectory = rootB, RetentionCount = 30 }),
        new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = spikeConn,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        }));
    await restoreService.RestoreAsync(dump);
    using (var check = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = spikeConn,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true
    }))
    {
        var count = check.Queryable<SpikeBackupEntity>().Count();
        Pass("MySQL逻辑导出与还原", dumpText.Contains("insert into") && count == 3,
            $"导出含INSERT={dumpText.Contains("insert into")}, 还原后行数={count}");
    }
}
catch (Exception ex)
{
    Pass("MySQL逻辑导出与还原", false, ex.ToString());
}

// ================= Phase C：平台手动备份端点 =================
const int PlatformPort = 5128;
var platformBackupDir = Path.Combine(Path.GetTempPath(), "station-m41-platform-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(platformBackupDir);
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
        ["STATION__DB__CONNECTIONSTRING"] = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
        ["STATION__BACKUP__BACKUPDIRECTORY"] = platformBackupDir,
        ["STATION__BACKUP__RETENTIONCOUNT"] = "30"
    }
});

try
{
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

    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}") };
    var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "Admin@123" });
    login.EnsureSuccessStatusCode();
    var create = await http.PostAsync("/api/v1/backups", null);
    var listResp = await http.GetAsync("/api/v1/backups");
    var files = Directory.GetFiles(platformBackupDir, "station_backup_*.sql");
    var logs = await http.GetFromJsonAsync<AuditPage>("/api/v1/audit-logs?page=1&size=20&keyword=backup");
    Pass("平台手动备份端点", create.StatusCode == HttpStatusCode.OK &&
                            listResp.StatusCode == HttpStatusCode.OK &&
                            files.Length == 1 &&
                            logs!.Data!.Items!.Any(x => x.OperationType == "backup.manual"),
        $"HTTP {(int)create.StatusCode}, 备份文件={files.Length}, 审计backup.manual={(logs!.Data!.Items!.Any(x => x.OperationType == "backup.manual"))}");
}
catch (Exception ex)
{
    Pass("平台手动备份端点", false, ex.ToString());
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

    try
    {
        Directory.Delete(rootA, true);
        Directory.Delete(rootB, true);
        Directory.Delete(platformBackupDir, true);
    }
    catch
    {
    }
}

Console.WriteLine();
Console.WriteLine("================ M41 数据备份 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;

[SugarTable("spike_backup_entity")]
internal sealed class SpikeBackupEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;
}

internal sealed record AuditPage(bool Success, int Code, string Message, AuditData? Data);

internal sealed record AuditData(int PageIndex, int PageSize, long TotalCount, List<AuditItem>? Items);

internal sealed record AuditItem(long Id, string? OperatorAccount, string? OperatorName, long? DeptId, string? SourceIp, string OperationType, string? Target, string? Detail, int Result, DateTime CreatedAt);
