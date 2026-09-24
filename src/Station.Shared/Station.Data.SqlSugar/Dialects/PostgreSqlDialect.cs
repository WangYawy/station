using Station.Data.Abstractions;

namespace Station.Data.SqlSugar.Dialects;

/// <summary>PostgreSQL 方言。</summary>
public sealed class PostgreSqlDialect : IDbDialect
{
    /// <inheritdoc />
    public DbProvider Provider => DbProvider.PostgreSQL;

    /// <inheritdoc />
    public string GetVersionSql() => "select version()";

    /// <inheritdoc />
    public string GetTimestampColumnType() => "timestamp";

    /// <inheritdoc />
    public string GetIndexListSql(string tableName) =>
        $"select indexname from pg_indexes where tablename = '{tableName}' order by indexname";

    /// <inheritdoc />
    public string GetTableListSql(string? schema = null) =>
        $"select tablename from pg_tables where schemaname = {(schema is null ? "current_schema()" : $"'{schema}'")} order by tablename";
}
