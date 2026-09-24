using SqlSugar;
using Station.Data;
using Station.Data.Abstractions;

namespace Station.Data.SqlSugar;

/// <summary>
/// SqlSugar 客户端工厂。
/// </summary>
public interface ISqlSugarFactory
{
    /// <summary>创建独立客户端（用于 UnitOfWork 事务隔离，非线程共享）。</summary>
    ISqlSugarClient CreateClient(DbOptions options, bool autoCloseConnection = true);

    /// <summary>创建线程安全共享作用域（用于常规读写路径，单例注册）。</summary>
    ISqlSugarClient CreateScope(DbOptions options);

    /// <summary>构建 <see cref="ConnectionConfig"/>，供外部创建自定义客户端。</summary>
    ConnectionConfig BuildConfig(DbOptions options, bool autoCloseConnection = true);
}
