namespace Station.Data.Abstractions;

/// <summary>
/// 产品支持的数据库提供程序。<br/>
/// 采集站本地：Sqlite / Kingbase；平台侧：MySql / PostgreSQL / Kingbase。
/// </summary>
public enum DbProvider
{
    /// <summary>SQLite（嵌入式文件库）。</summary>
    Sqlite,

    /// <summary>人大金仓（KingbaseES）。</summary>
    Kingbase,

    /// <summary>MySQL / MariaDB。</summary>
    MySql,

    /// <summary>PostgreSQL。</summary>
    PostgreSQL
}
