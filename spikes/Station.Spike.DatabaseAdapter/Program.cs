using SqlSugar;

// ---------------------------------------------------------------------------
// M3 Spike: 四库通用适配验证
//   kingbase   : KingbaseES V8R6C9B14 (WSL2 Ubuntu, 端口 54321, ORACLE 模式)
//   mysql      : MySQL 8.0 (WSL2 Ubuntu, 端口 3306)
//   postgresql : PostgreSQL 15+ (WSL2 Ubuntu, 端口 5432)
//   sqlite     : 本地文件库 (Microsoft.Data.Sqlite)
// ORM: SqlSugarCore 5.1.4.216；驱动: Kdbndp 8.0.0 / MySqlConnector 2.3.7 /
//      Npgsql 8.0.9 / Microsoft.Data.Sqlite 8.0.30
// 用法: dotnet run --project spikes/Station.Spike.DatabaseAdapter -c Release -- --db mysql
//       dotnet run ... -- --db postgresql --conn "Host=...;..."
// ---------------------------------------------------------------------------

var dbArg = "";
var connOverride = "";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length) dbArg = args[i + 1];
    if (args[i] == "--conn" && i + 1 < args.Length) connOverride = args[i + 1];
}

var dbName = dbArg.ToLowerInvariant() switch
{
    "mysql" => "mysql",
    "postgresql" or "pg" => "postgresql",
    "sqlite" => "sqlite",
    _ => "kingbase"
};

var defaultConn = dbName switch
{
    "mysql" => "Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None",
    "postgresql" => "Host=localhost;Port=5432;Database=station_spike;Username=station;Password=Station@123",
    "sqlite" => $"Data Source={Path.Combine(AppContext.BaseDirectory, "spike_sqlite.db")}",
    _ => "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123"
};
var connStr = string.IsNullOrWhiteSpace(connOverride) ? defaultConn : connOverride;

var dbType = dbName switch
{
    "mysql" => DbType.MySql,
    "postgresql" => DbType.PostgreSQL,
    "sqlite" => DbType.Sqlite,
    _ => DbType.Kdbndp
};

var versionSql = dbName == "sqlite" ? "select sqlite_version()" : "select version()";
var indexSql = dbName switch
{
    "mysql" => "select distinct index_name from information_schema.statistics where table_schema = database() and table_name = 'spike_file_record' order by index_name",
    "sqlite" => "select name from pragma_index_list('spike_file_record') order by name",
    _ => "select indexname from pg_indexes where tablename = 'spike_file_record' order by indexname"
};

var results = new List<(string Step, bool Ok, string Detail)>();
var sw = System.Diagnostics.Stopwatch.StartNew();

void Pass(string step, string detail) => results.Add((step, true, detail));
void Fail(string step, Exception ex) => results.Add((step, false, ex.Message));

var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = connStr,
    DbType = dbType,
    IsAutoCloseConnection = true,
    InitKeyType = InitKeyType.Attribute
});

// ---------- 1. 连接 + 版本 ----------
try
{
    var ver = await db.Ado.GetStringAsync(versionSql);
    Pass("SqlSugar 连接", ver);
}
catch (Exception ex)
{
    Fail("SqlSugar 连接", ex);
}

// ---------- 2. CodeFirst 建表（含索引） ----------
try
{
    try { db.DbMaintenance.DropTable<SpikeFileRecord>(); } catch { /* 首次运行无表，忽略 */ }
    db.CodeFirst.InitTables<SpikeFileRecord>();
    var cols = db.DbMaintenance.GetColumnInfosByTableName("spike_file_record");
    Pass("CodeFirst 建表", $"表 spike_file_record 列数={cols.Count}");
}
catch (Exception ex)
{
    Fail("CodeFirst 建表", ex);
}

// ---------- 3. 批量插入 100 条 ----------
try
{
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
    var affected = await db.Insertable(rows).ExecuteCommandAsync();
    Pass("批量插入", $"100 条, 影响行数={affected}");
}
catch (Exception ex)
{
    Fail("批量插入", ex);
}

// ---------- 4. 条件查询 + 分页 ----------
try
{
    var total = 0;
    var page = db.Queryable<SpikeFileRecord>()
        .Where(x => x.Status == 1)
        .OrderBy(x => x.Id, OrderByType.Asc)
        .ToPageList(2, 10, ref total);
    Pass("条件分页", $"status=1 总数={total}, 第2页取回={page.Count}, 首条Id={page.FirstOrDefault()?.Id}");
}
catch (Exception ex)
{
    Fail("条件分页", ex);
}

// ---------- 5. 事务：回滚与提交 ----------
try
{
    var before = await db.Queryable<SpikeFileRecord>().CountAsync();

    var rollbackOk = db.Ado.UseTran(() =>
    {
        db.Insertable(new SpikeFileRecord { Id = 100001, FileNo = "TX-ROLLBACK", FileName = "x.mp4", FileSize = 1, CollectedAt = DateTime.Now, Status = 9 })
            .ExecuteCommand();
        throw new InvalidOperationException("trigger rollback");
    }).IsSuccess == false;
    var afterRollback = await db.Queryable<SpikeFileRecord>().CountAsync();

    var commitOk = db.Ado.UseTran(() =>
    {
        db.Insertable(new SpikeFileRecord { Id = 100002, FileNo = "TX-COMMIT", FileName = "y.mp4", FileSize = 1, CollectedAt = DateTime.Now, Status = 9 })
            .ExecuteCommand();
    }).IsSuccess;
    var afterCommit = await db.Queryable<SpikeFileRecord>().CountAsync();

    Pass("事务", $"回滚正确={rollbackOk && before == afterRollback}, 提交正确={commitOk && afterCommit == before + 1}");
}
catch (Exception ex)
{
    Fail("事务", ex);
}

// ---------- 6. 索引验证 ----------
try
{
    var indexes = await db.Ado.SqlQueryAsync<string>(indexSql);
    Pass("索引", string.Join(", ", indexes));
}
catch (Exception ex)
{
    Fail("索引", ex);
}

// ---------- 7. 清理 ----------
try
{
    db.DbMaintenance.DropTable<SpikeFileRecord>();
    Pass("清理", "已删除测试表 spike_file_record");
}
catch (Exception ex)
{
    Fail("清理", ex);
}

sw.Stop();

Console.WriteLine();
Console.WriteLine($"================ {dbName} 适配验证结果 ================");
var failed = 0;
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
    if (!ok) failed++;
}
Console.WriteLine("--------------------------------------------------------");
Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项, 耗时 {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"DbType={dbType}, Conn={connStr}");
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
