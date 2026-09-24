using Station.Data.Abstractions;

namespace Station.Data.SqlSugar.Dialects;

/// <summary>数据库方言工厂。</summary>
public static class DbDialectFactory
{
    /// <summary>根据数据库提供程序创建对应方言。</summary>
    public static IDbDialect Create(DbProvider provider) => provider switch
    {
        DbProvider.Sqlite => new SqliteDialect(),
        DbProvider.Kingbase => new KingbaseDialect(),
        DbProvider.MySql => new MySqlDialect(),
        DbProvider.PostgreSQL => new PostgreSqlDialect(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}
