namespace Station.Infrastructure.Db;

/// <summary>
/// 数据库配置，对应配置节 <c>Station:Db</c>。
/// </summary>
public sealed class DbOptions
{
    public const string SectionName = "Station:Db";

    public DbProvider Provider { get; set; } = DbProvider.Sqlite;

    public string ConnectionString { get; set; } = string.Empty;
}
