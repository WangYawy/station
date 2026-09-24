using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Data.Abstractions;
using Station.Data.Backup;
using Station.Data.IdGeneration;
using Station.Data.Paging;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Dialects;
using Station.Data.SqlSugar.Repositories;

namespace Station.Data.SqlSugar;

/// <summary>
/// 数据访问层 DI 扩展（SqlSugar 实现）。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册数据访问层（SqlSugar 实现）。<br/>
    /// 默认读取配置节 <c>Station:Data</c>（Db / Paging / Snowflake）；<br/>
    /// 可通过 <paramref name="configureDb"/> 对 <see cref="DbOptions"/> 做后置加工（如 SQLite 路径重定位）。<br/>
    /// 注意：本方法仅注册基础设施；建表 / 补列等初始化动作由产品侧在启动流程中显式调用 <see cref="IDatabaseInitializer"/>。
    /// </summary>
    public static IServiceCollection AddStationDataSqlSugar(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbOptions>? configureDb = null)
    {
        // ==========================================
        // 1. 分页配置
        // ==========================================
        services.AddOptions<PagingOptions>()
                .Bind(configuration.GetSection(PagingOptions.SectionName))
                .ValidateOnStart();

        // ==========================================
        // 2. 数据库配置（先绑定后加工，允许产品侧重定位连接串等）
        // ==========================================
        services.AddOptions<DbOptions>()
                .Bind(configuration.GetSection(DbOptions.SectionName))
                .Configure(opts => configureDb?.Invoke(opts))
                .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DbOptions>>().Value);

        services.AddOptions<BackupOptions>()
                .Bind(configuration.GetSection(BackupOptions.SectionName))
                .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<BackupOptions>>().Value);

        // ==========================================
        // 3. 雪花算法（ID 生成器）
        // ==========================================
        services.AddOptions<SnowFlakeOptions>()
                .Bind(configuration.GetSection(SnowFlakeOptions.SectionName))
                .ValidateOnStart();
        services.AddSingleton<IIdGenerator>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<SnowFlakeOptions>>().Value;
            return new SnowflakeIdGenerator(o.WorkId, o.DatacenterId);
        });

        // ==========================================
        // 4. SqlSugar 工厂（单例）
        // ==========================================
        services.AddSingleton<ISqlSugarFactory, SqlSugarFactory>();

        // ==========================================
        // 5. 短连接客户端（供 IRepository 使用，Singleton）
        //    - SQLite：常驻连接（避免 reader is closed）
        //    - 其他库：autoClose（避免连接池被常驻连接耗尽）
        // ==========================================
        services.AddSingleton<ISqlSugarClient>(sp =>
        {
            var factory = sp.GetRequiredService<ISqlSugarFactory>();
            var opts = sp.GetRequiredService<DbOptions>();
            return factory.CreateScope(opts);
        });

        // ==========================================
        // 6. 长连接客户端（供 ILoopRepository 使用，Scoped）
        //    强制 autoCloseConnection: false
        // ==========================================
        services.AddScoped<ILoopSqlSugarClient>(sp =>
        {
            var factory = sp.GetRequiredService<ISqlSugarFactory>();
            var opts = sp.GetRequiredService<DbOptions>();
            var config = factory.BuildConfig(opts, autoCloseConnection: false);
            return new LoopSqlSugarClient(config);
        });

        // ==========================================
        // 7. 方言 & 初始化器 & 健康检查
        // ==========================================
        services.AddSingleton<IDbDialect>(sp =>
            DbDialectFactory.Create(sp.GetRequiredService<DbOptions>().Provider));
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IDatabaseHealthService, DatabaseHealthService>();

        // ==========================================
        // 8. UnitOfWork（独立短连接，事务隔离）
        // ==========================================
        services.AddScoped<IUnitOfWork>(sp =>
        {
            var factory = sp.GetRequiredService<ISqlSugarFactory>();
            var opts = sp.GetRequiredService<DbOptions>();
            var paging = sp.GetRequiredService<IOptions<PagingOptions>>();
            return new UnitOfWork(factory.CreateClient(opts), paging);
        });

        // ==========================================
        // 9. 仓储
        // ==========================================
        services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
        services.AddScoped(typeof(ILoopRepository<>), typeof(LoopRepositoryBase<>));

        return services;
    }
}
