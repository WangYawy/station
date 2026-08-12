using System.Diagnostics;
using Kdbndp;
using SqlSugar;

// ---------------------------------------------------------------------------
// M2 Spike: Kingbase (人大金仓 V8R6C9B14, WSL2 Ubuntu) 三库适配验证
// 验证链路: Kdbndp 直连 + SqlSugar DbType.Kingbase(CodeFirst/分页/事务/索引)
// 连接串: Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123
// ---------------------------------------------------------------------------

var connStr = Environment.GetEnvironmentVariable("STATION_KB_CONN")
              ?? "Host=localhost;Port=54321;Database=test;Username=system;Password=Test@123";

var results = new List<(string Step, bool Ok, string Detail)>();
var sw = Stopwatch.StartNew();

void Pass(string step, string detail) => results.Add((step, true, detail));
void Fail(string step, Exception ex) => results.Add((step, false, ex.Message));

// ---------- 1. Kdbndp 原生驱动直连 ----------
try
{
    await using var raw = new KdbndpConnection(connStr);
    await raw.OpenAsync();
    await using var cmd = raw.CreateCommand();
    cmd.CommandText = "select version()";
    var ver = (string)(await cmd.ExecuteScalarAsync())!;
    Pass("Kdbndp 直连", ver);
}
catch (Exception ex)
{
    Fail("Kdbndp 直连", ex);
}

// ---------- 2. SqlSugar DbType.Kingbase ----------
var db = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = connStr,
    DbType = DbType.Kdbndp,
    IsAutoCloseConnection = true,
    InitKeyType = InitKeyType.Attribute
});

try
{
    var sugarVer = await db.Ado.GetStringAsync("select version()");
    Pass("SqlSugar 连接", sugarVer);
}
catch (Exception ex)
{
    Fail("SqlSugar 连接", ex);
}

// ---------- 3. CodeFirst 建表（含索引） ----------
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

// ---------- 4. 批量插入 100 条 ----------
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

// ---------- 5. 条件查询 + 分页 ----------
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

// ---------- 6. 事务：回滚与提交 ----------
try
{
    var before = await db.Queryable<SpikeFileRecord>().CountAsync();

    // 6.1 回滚
    var rollbackOk = db.Ado.UseTran(() =>
    {
        db.Insertable(new SpikeFileRecord { Id = 100001, FileNo = "TX-ROLLBACK", FileName = "x.mp4", FileSize = 1, CollectedAt = DateTime.Now, Status = 9 })
            .ExecuteCommand();
        throw new InvalidOperationException("trigger rollback");
    }).IsSuccess == false;
    var afterRollback = await db.Queryable<SpikeFileRecord>().CountAsync();

    // 6.2 提交
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

// ---------- 7. 索引验证 ----------
try
{
    var indexes = await db.Ado.SqlQueryAsync<string>(
        "select indexname from pg_indexes where tablename = 'spike_file_record' order by indexname");
    Pass("索引", string.Join(", ", indexes));
}
catch (Exception ex)
{
    Fail("索引", ex);
}

// ---------- 8. 清理 ----------
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
Console.WriteLine("================ M2 Kingbase Spike 结果 ================");
var failed = 0;
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
    if (!ok) failed++;
}
Console.WriteLine($"--------------------------------------------------------");
Console.WriteLine($"总计 {results.Count} 项, 失败 {failed} 项, 耗时 {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"金仓: {db.CurrentConnectionConfig.DbType}");
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
