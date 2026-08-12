using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;

// ---------------------------------------------------------------------------
// M4 Spike: 数据访问层（Station.Infrastructure）四库验证
// 覆盖：SqlSugar 工厂 / IDbDialect / DatabaseInitializer / IRepository<T> /
//       IUnitOfWork(事务) / AddStationDatabase(DI + 配置绑定)
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
    "sqlite" => (DbProvider.Sqlite, $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_sqlite.db")}"),
    _ => (DbProvider.Kingbase, "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123")
};
var connStr = string.IsNullOrWhiteSpace(connOverride) ? defaultConn : connOverride;

var results = new List<(string Step, bool Ok, string Detail)>();
var sw = System.Diagnostics.Stopwatch.StartNew();

void Pass(string step, string detail) => results.Add((step, true, detail));
void Fail(string step, Exception ex) => results.Add((step, false, ex.Message));

var options = new DbOptions { Provider = provider, ConnectionString = connStr };

// ---------- 1. 工厂 + 方言 + 初始化器：连接与版本 ----------
var factory = new SqlSugarFactory();
var scope = factory.CreateScope(options);
var dialect = DbDialectFactory.Create(options.Provider);
var initializer = new DatabaseInitializer(scope, dialect);

try
{
    var version = await initializer.GetVersionAsync();
    Pass("连接+版本", version);
}
catch (Exception ex)
{
    Fail("连接+版本", ex);
}

// ---------- 2. CodeFirst 建表 ----------
try
{
    try { scope.DbMaintenance.DropTable<SpikeFileRecord>(); } catch { /* 首次运行无表 */ }
    initializer.EnsureCreated(typeof(SpikeFileRecord));
    var tables = await initializer.GetTablesAsync();
    Pass("CodeFirst 建表", $"表存在={tables.Contains("spike_file_record")}, 库内表数={tables.Count}");
}
catch (Exception ex)
{
    Fail("CodeFirst 建表", ex);
}

// ---------- 3. 通用仓储：批量插入 + 分页 ----------
try
{
    var repo = new RepositoryBase<SpikeFileRecord>(scope);
    var rows = Enumerable.Range(1, 100).Select(i => new SpikeFileRecord
    {
        Id = i,
        FileNo = $"KB-2026-{i:D4}",
        FileName = $"recording_{i:D4}.mp4",
        FileSize = 1024L * 1024 * i,
        CollectedAt = DateTime.Now.AddMinutes(-i),
        Status = i % 3,
        Remark = i % 10 == 0 ? null : $"remark-{i}"
    }).ToList();

    var affected = await repo.InsertRangeAsync(rows);
    var page = await repo.ToPageAsync(2, 10, x => x.Status == 1, x => x.Id, OrderByType.Asc);
    Pass("仓储分页", $"插入={affected}, status=1总数={page.Total}, 第2页={page.Items.Count}, 首条Id={page.Items.FirstOrDefault()?.Id}");
}
catch (Exception ex)
{
    Fail("仓储分页", ex);
}

// ---------- 4. UnitOfWork：回滚与提交 ----------
try
{
    var repo = new RepositoryBase<SpikeFileRecord>(scope);
    var before = await repo.CountAsync();

    var rollbackOk = false;
    using (var uow = new UnitOfWork(factory.CreateClient(options)))
    {
        try
        {
            await uow.UseTranAsync<bool>(async () =>
            {
                var r = uow.GetRepository<SpikeFileRecord>();
                await r.InsertAsync(new SpikeFileRecord { Id = 100001, FileNo = "TX-ROLLBACK", FileName = "x.mp4", FileSize = 1, CollectedAt = DateTime.Now, Status = 9 });
                throw new InvalidOperationException("trigger rollback");
            });
        }
        catch (InvalidOperationException)
        {
            rollbackOk = true;
        }
    }

    var afterRollback = await repo.CountAsync();

    var commitOk = false;
    using (var uow = new UnitOfWork(factory.CreateClient(options)))
    {
        commitOk = await uow.UseTranAsync(async () =>
        {
            var r = uow.GetRepository<SpikeFileRecord>();
            await r.InsertAsync(new SpikeFileRecord { Id = 100002, FileNo = "TX-COMMIT", FileName = "y.mp4", FileSize = 1, CollectedAt = DateTime.Now, Status = 9 });
            return true;
        });
    }

    var afterCommit = await repo.CountAsync();
    Pass("UnitOfWork 事务", $"回滚正确={rollbackOk && before == afterRollback}, 提交正确={commitOk && afterCommit == before + 1}");
}
catch (Exception ex)
{
    Fail("UnitOfWork 事务", ex);
}

// ---------- 5. 方言：索引目录 ----------
try
{
    var indexes = await initializer.GetIndexesAsync("spike_file_record");
    Pass("方言-索引", string.Join(", ", indexes));
}
catch (Exception ex)
{
    Fail("方言-索引", ex);
}

// ---------- 6. DI + 配置绑定（AddStationDatabase） ----------
try
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Station:Db:Provider"] = provider.ToString(),
            ["Station:Db:ConnectionString"] = connStr
        })
        .Build();

    var services = new ServiceCollection();
    services.AddStationDatabase(config);
    await using var sp = services.BuildServiceProvider();

    var diInitializer = sp.GetRequiredService<IDatabaseInitializer>();
    var diVersion = await diInitializer.GetVersionAsync();
    var diRepo = sp.GetRequiredService<IRepository<SpikeFileRecord>>();
    var diCount = await diRepo.CountAsync();
    var diUow = sp.GetRequiredService<IUnitOfWork>();

    Pass("DI+配置绑定", $"版本={diVersion[..Math.Min(28, diVersion.Length)]}..., 计数={diCount}, UoW类型={diUow.GetType().Name}");
}
catch (Exception ex)
{
    Fail("DI+配置绑定", ex);
}

// ---------- 7. 清理 ----------
try
{
    scope.DbMaintenance.DropTable<SpikeFileRecord>();
    Pass("清理", "已删除测试表 spike_file_record");
}
catch (Exception ex)
{
    Fail("清理", ex);
}

sw.Stop();

Console.WriteLine();
Console.WriteLine($"================ {provider} 数据访问层验证 ================");
var failed = 0;
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
    if (!ok) failed++;
}
Console.WriteLine("--------------------------------------------------------");
Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项, 耗时 {sw.Elapsed.TotalSeconds:F1}s");
return failed == 0 ? 0 : 1;

[SugarTable("spike_file_record")]
[SugarIndex("idx_spike_status", nameof(SpikeFileRecord.Status), OrderByType.Asc)]
[SugarIndex("idx_spike_collected", nameof(SpikeFileRecord.CollectedAt), OrderByType.Desc)]
public class SpikeFileRecord
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string FileNo { get; set; } = "";

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = "";

    public long FileSize { get; set; }

    public DateTime CollectedAt { get; set; }

    public int Status { get; set; }

    [SugarColumn(IsNullable = true, Length = 255)]
    public string? Remark { get; set; }
}
