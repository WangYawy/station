using SqlSugar;
using Station.Infrastructure.Db;

namespace Station.Infrastructure;

/// <summary>
/// 数据库初始化服务：CodeFirst 建表、版本/索引/表目录诊断。
/// </summary>
public interface IDatabaseInitializer
{
    void EnsureCreated(params Type[] entityTypes);

    /// <summary>跨库幂等补列：表已存在且缺列时追加（如新增字段的演进）。</summary>
    void EnsureColumn(string tableName, string columnName);

    Task<string> GetVersionAsync();

    Task<IReadOnlyList<string>> GetIndexesAsync(string tableName);

    Task<IReadOnlyList<string>> GetTablesAsync();
}

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly IDbDialect _dialect;

    public DatabaseInitializer(ISqlSugarClient db, IDbDialect dialect)
    {
        _db = db;
        _dialect = dialect;
    }

    public void EnsureCreated(params Type[] entityTypes)
    {
        if (entityTypes.Length > 0)
        {
            _db.CodeFirst.InitTables(entityTypes);
        }
    }

    public void EnsureColumn(string tableName, string columnName)
    {
        var columns = _db.DbMaintenance.GetColumnInfosByTableName(tableName);
        if (columns.Any(c => string.Equals(c.DbColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _db.DbMaintenance.AddColumn(tableName, new DbColumnInfo
        {
            DbColumnName = columnName,
            DataType = _dialect.GetTimestampColumnType(),
            IsNullable = true
        });
    }

    public Task<string> GetVersionAsync() => _db.Ado.GetStringAsync(_dialect.GetVersionSql());

    public async Task<IReadOnlyList<string>> GetIndexesAsync(string tableName) =>
        await _db.Ado.SqlQueryAsync<string>(_dialect.GetIndexListSql(tableName));

    public async Task<IReadOnlyList<string>> GetTablesAsync() =>
        await _db.Ado.SqlQueryAsync<string>(_dialect.GetTableListSql());
}
