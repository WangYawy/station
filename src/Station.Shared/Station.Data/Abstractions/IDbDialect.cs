namespace Station.Data.Abstractions;

/// <summary>
/// 数据库方言抽象：收敛四库在 版本查询 / 索引目录 / 表目录 上的 SQL 差异。<br/>
/// 业务代码不得直接拼方言 SQL，统一走此接口。
/// </summary>
public interface IDbDialect
{
    /// <summary>当前方言对应的数据库提供程序。</summary>
    DbProvider Provider { get; }

    /// <summary>获取数据库版本 SQL。</summary>
    string GetVersionSql();

    /// <summary>时间列类型（用于跨库补列等 DDL）。</summary>
    string GetTimestampColumnType();

    /// <summary>获取表索引 SQL。</summary>
    string GetIndexListSql(string tableName);

    /// <summary>获取数据库表目录 SQL；<paramref name="schema"/> 为 null 时使用当前 schema。</summary>
    string GetTableListSql(string? schema = null);
}
