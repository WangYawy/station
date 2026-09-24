using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Data.Abstractions;

namespace Station.Data;

/// <summary>
/// 数据库初始化服务：CodeFirst 建表、版本/索引/表目录诊断。
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>CodeFirst 建表（实体类型集合）。</summary>
    void EnsureCreated(params Type[] entityTypes);

    /// <summary>跨库幂等补列：表已存在且缺列时追加（如新增字段的演进）。</summary>
    void EnsureColumn(string tableName, string columnName, string? dataType = null);

    /// <summary>获取数据库版本。</summary>
    Task<string> GetVersionAsync();

    /// <summary>获取指定表的索引列表。</summary>
    Task<IReadOnlyList<string>> GetIndexesAsync(string tableName);

    /// <summary>获取当前库中的表目录。</summary>
    Task<IReadOnlyList<string>> GetTablesAsync();
}

/// <inheritdoc />
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly IDbDialect _dialect;
    private readonly DbOptions _options;

    public DatabaseInitializer(
        ISqlSugarClient db,
        IDbDialect dialect,
        IOptions<DbOptions> options)
    {
        _db = db;
        _dialect = dialect;
        _options = options.Value;
    }

    /// <inheritdoc />
    public void EnsureCreated(params Type[] entityTypes)
    {
        if (_dialect.Provider == DbProvider.Sqlite && _options.EnableSqliteWal)
        {
            TryEnableSqliteWal();
        }

        if (entityTypes.Length > 0)
        {
            _db.CodeFirst.InitTables(entityTypes);
        }
    }

    /// <summary>SQLite 并发优化：WAL 模式 + busy_timeout，避免采集后台与查询并发时锁竞争/连接关闭。</summary>
    private void TryEnableSqliteWal()
    {
        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                _db.CurrentConnectionConfig.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000; PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();
        }
        catch
        {
            // WAL 不可用时保持默认日志模式，不影响功能
        }
    }

    /// <inheritdoc />
    public void EnsureColumn(string tableName, string columnName, string? dataType = null)
    {
        var columns = _db.DbMaintenance.GetColumnInfosByTableName(tableName);
        if (columns.Any(c => string.Equals(c.DbColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _db.DbMaintenance.AddColumn(tableName, new DbColumnInfo
        {
            DbColumnName = columnName,
            DataType = dataType ?? _dialect.GetTimestampColumnType(),
            IsNullable = true
        });
    }

    /// <inheritdoc />
    public Task<string> GetVersionAsync() => _db.Ado.GetStringAsync(_dialect.GetVersionSql());

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetIndexesAsync(string tableName) =>
        await _db.Ado.SqlQueryAsync<string>(_dialect.GetIndexListSql(tableName));

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTablesAsync() =>
        await _db.Ado.SqlQueryAsync<string>(_dialect.GetTableListSql());
}
