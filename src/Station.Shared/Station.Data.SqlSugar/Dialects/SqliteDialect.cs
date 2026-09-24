using Station.Data.Abstractions;

namespace Station.Data.SqlSugar.Dialects;

/// <summary>SQLite 方言。</summary>
public sealed class SqliteDialect : IDbDialect
{
    /// <inheritdoc />
    public DbProvider Provider => DbProvider.Sqlite;

    /// <inheritdoc />
    public string GetVersionSql() => "select sqlite_version()";

    /// <inheritdoc />
    public string GetTimestampColumnType() => "datetime";

    /// <inheritdoc />
    public string GetIndexListSql(string tableName) =>
        $"select name from pragma_index_list('{tableName}') order by name";

    /// <inheritdoc />
    public string GetTableListSql(string? schema = null) =>
        "select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name";
}
