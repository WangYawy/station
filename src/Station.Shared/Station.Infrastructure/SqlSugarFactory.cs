using SqlSugar;
using Station.Infrastructure.Db;

namespace Station.Infrastructure;

/// <summary>
/// SqlSugar 客户端工厂：把 <see cref="SnowFlakeOptions"/> 转换为对应提供程序的客户端实例。
/// </summary>
public interface ISqlSugarFactory
{
    /// <summary>创建独立客户端（用于 UnitOfWork 事务隔离，非线程共享）。</summary>
    ISqlSugarClient CreateClient(DbOptions options, bool autoCloseConnection = true);

    /// <summary>创建线程安全共享作用域（用于常规读写路径，单例注册）。</summary>
    ISqlSugarClient CreateScope(DbOptions options);
}

public sealed class SqlSugarFactory : ISqlSugarFactory
{
    public ISqlSugarClient CreateClient(DbOptions options, bool autoCloseConnection = true)
    {
        var config = BuildConfig(options);
        config.IsAutoCloseConnection = autoCloseConnection;
        return new SqlSugarClient(config);
    }

    public ISqlSugarClient CreateScope(DbOptions options)
    {
        var config = BuildConfig(options);
        // SQLite 保持连接常驻，避免异步查询中 auto-close 导致 "reader is closed"；
        // 服务端数据库（MySQL/PostgreSQL/Kingbase）使用 auto-close，避免连接池被常驻连接耗尽
        config.IsAutoCloseConnection = options.Provider != DbProvider.Sqlite;
        return new SqlSugarScope(config);
    }

    private static ConnectionConfig BuildConfig(DbOptions options) => new()
    {
        ConnectionString = options.ConnectionString,
        DbType = ToSqlSugarDbType(options.Provider),
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute,
        MoreSettings = new ConnMoreSettings
        {
            // Kingbase/PostgreSQL 统一小写表名列名，避免 ORACLE 模式默认大写的差异
            IsAutoToUpper = false
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
