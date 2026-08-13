namespace Station.Infrastructure.Db;

/// <summary>
/// 数据库方言抽象：收敛四库在 版本查询 / 索引目录 / 表目录 上的 SQL 差异。
/// 业务代码不得直接拼方言 SQL，统一走此接口。
/// </summary>
public interface IDbDialect
{
    DbProvider Provider { get; }

    string GetVersionSql();

    /// <summary>时间列类型（用于跨库补列等 DDL）。</summary>
    string GetTimestampColumnType();

    string GetIndexListSql(string tableName);

    string GetTableListSql(string? schema = null);
}

public sealed class SqliteDialect : IDbDialect
{
    public DbProvider Provider => DbProvider.Sqlite;

    public string GetVersionSql() => "select sqlite_version()";

    public string GetTimestampColumnType() => "datetime";

    public string GetIndexListSql(string tableName) =>
        $"select name from pragma_index_list('{tableName}') order by name";

    public string GetTableListSql(string? schema = null) =>
        "select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name";
}

public sealed class KingbaseDialect : IDbDialect
{
    public DbProvider Provider => DbProvider.Kingbase;

    public string GetVersionSql() => "select version()";

    public string GetTimestampColumnType() => "timestamp";

    public string GetIndexListSql(string tableName) =>
        $"select indexname from pg_indexes where tablename = '{tableName}' order by indexname";

    public string GetTableListSql(string? schema = null) =>
        $"select tablename from pg_tables where schemaname = {(schema is null ? "current_schema()" : $"'{schema}'")} order by tablename";
}

public sealed class MySqlDialect : IDbDialect
{
    public DbProvider Provider => DbProvider.MySql;

    public string GetVersionSql() => "select version()";

    public string GetTimestampColumnType() => "datetime";

    public string GetIndexListSql(string tableName) =>
        $"select distinct index_name from information_schema.statistics where table_schema = database() and table_name = '{tableName}' order by index_name";

    public string GetTableListSql(string? schema = null) =>
        $"select table_name from information_schema.tables where table_schema = {(schema is null ? "database()" : $"'{schema}'")} order by table_name";
}

public sealed class PostgreSqlDialect : IDbDialect
{
    public DbProvider Provider => DbProvider.PostgreSQL;

    public string GetVersionSql() => "select version()";

    public string GetTimestampColumnType() => "timestamp";

    public string GetIndexListSql(string tableName) =>
        $"select indexname from pg_indexes where tablename = '{tableName}' order by indexname";

    public string GetTableListSql(string? schema = null) =>
        $"select tablename from pg_tables where schemaname = {(schema is null ? "current_schema()" : $"'{schema}'")} order by tablename";
}

public static class DbDialectFactory
{
    public static IDbDialect Create(DbProvider provider) => provider switch
    {
        DbProvider.Sqlite => new SqliteDialect(),
        DbProvider.Kingbase => new KingbaseDialect(),
        DbProvider.MySql => new MySqlDialect(),
        DbProvider.PostgreSQL => new PostgreSqlDialect(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}
