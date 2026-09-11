namespace Station.Infrastructure.Db;

/// <summary>
/// 产品支持的数据库提供程序。
/// 采集站本地：Sqlite / Kingbase；平台侧：MySql / PostgreSQL / Kingbase。
/// </summary>
public enum DbProvider
{
    Sqlite,
    Kingbase,
    MySql,
    PostgreSQL
}
