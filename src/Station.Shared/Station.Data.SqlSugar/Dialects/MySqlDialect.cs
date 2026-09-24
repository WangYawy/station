using Station.Data.Abstractions;

namespace Station.Data.SqlSugar.Dialects;

/// <summary>MySQL 方言。</summary>
public sealed class MySqlDialect : IDbDialect
{
    /// <inheritdoc />
    public DbProvider Provider => DbProvider.MySql;

    /// <inheritdoc />
    public string GetVersionSql() => "select version()";

    /// <inheritdoc />
    public string GetTimestampColumnType() => "datetime";

    /// <inheritdoc />
    public string GetIndexListSql(string tableName) =>
        $"select distinct index_name from information_schema.statistics where table_schema = database() and table_name = '{tableName}' order by index_name";

    /// <inheritdoc />
    public string GetTableListSql(string? schema = null) =>
        $"select table_name from information_schema.tables where table_schema = {(schema is null ? "database()" : $"'{schema}'")} order by table_name";
}
