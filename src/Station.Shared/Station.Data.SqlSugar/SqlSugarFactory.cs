using SqlSugar;
using Station.Data.Abstractions;

namespace Station.Data.SqlSugar;

/// <inheritdoc />
public sealed class SqlSugarFactory : ISqlSugarFactory
{
    /// <inheritdoc />
    public ISqlSugarClient CreateClient(DbOptions options, bool autoCloseConnection = true)
    {
        var config = BuildConfig(options, autoCloseConnection);
        return new SqlSugarClient(config);
    }

    /// <inheritdoc />
    public ISqlSugarClient CreateScope(DbOptions options)
    {
        // SQLite 保持连接常驻，避免异步查询中 auto-close 导致 "reader is closed"；
        // 服务端数据库（MySQL/PostgreSQL/Kingbase）使用 auto-close，避免连接池被常驻连接耗尽。
        var config = BuildConfig(options, options.Provider != DbProvider.Sqlite);
        return new SqlSugarScope(config);
    }

    /// <inheritdoc />
    public ConnectionConfig BuildConfig(DbOptions options, bool autoCloseConnection = true) => new()
    {
        ConnectionString = options.ConnectionString,
        DbType = ToSqlSugarDbType(options.Provider),
        IsAutoCloseConnection = autoCloseConnection,
        InitKeyType = InitKeyType.Attribute,
        MoreSettings = new ConnMoreSettings
        {
            // Kingbase/PostgreSQL 大小写策略由 DbOptions 控制，默认小写
            IsAutoToUpper = options.IsAutoToUpper
        }
    };

    private static DbType ToSqlSugarDbType(DbProvider provider) => provider switch
    {
        DbProvider.Sqlite => DbType.Sqlite,
        DbProvider.Kingbase => DbType.Kdbndp,
        DbProvider.MySql => DbType.MySql,
        DbProvider.PostgreSQL => DbType.PostgreSQL,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}
