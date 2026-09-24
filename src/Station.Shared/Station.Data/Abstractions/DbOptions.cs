namespace Station.Data.Abstractions;

/// <summary>数据库配置，对应配置节 <c>Station:Data:Db</c>。</summary>
public sealed class DbOptions
{
    /// <summary>默认配置节名。</summary>
    public const string SectionName = "Data:Db";

    /// <summary>数据库提供程序，默认 SQLite。</summary>
    public DbProvider Provider { get; set; } = DbProvider.Sqlite;

    /// <summary>连接字符串（必填）。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Kingbase / PostgreSQL 表名列名大小写策略。<br/>
    /// <c>false</c>（默认）= 统一小写；<c>true</c> = 自动转大写。
    /// </summary>
    public bool IsAutoToUpper { get; set; } = false;

    /// <summary>
    /// SQLite 是否启用 WAL 日志模式 + busy_timeout（并发优化）。<br/>
    /// 默认 <c>true</c>；仅在 <see cref="Provider"/> = <see cref="DbProvider.Sqlite"/> 时生效。
    /// </summary>
    public bool EnableSqliteWal { get; set; } = true;
}
