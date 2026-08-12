using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Infrastructure.Db;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;

namespace Station.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册数据访问层：配置节 <c>Station:Db</c>（Provider + ConnectionString）。
    /// 提供 ISqlSugarClient（单例作用域）、IDbDialect、IDatabaseInitializer、IUnitOfWork、IRepository&lt;&gt;。
    /// </summary>
    public static IServiceCollection AddStationDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(DbOptions.SectionName);
        var options = section.Get<DbOptions>() ?? new DbOptions();

        services.Configure<DbOptions>(section);
        services.AddSingleton(options);
        services.AddSingleton<ISqlSugarFactory, SqlSugarFactory>();

        // 常规读写：线程安全共享作用域
        services.AddSingleton<ISqlSugarClient>(sp =>
            sp.GetRequiredService<ISqlSugarFactory>().CreateScope(options));

        services.AddSingleton<IDbDialect>(_ => DbDialectFactory.Create(options.Provider));
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.AddScoped<IPasswordHasher, Sm3PasswordHasher>();
        services.Configure<AuthSeedOptions>(configuration.GetSection("Station:Auth"));
        services.AddScoped<IAuthSeeder, AuthSeeder>();

        // UnitOfWork：独立客户端 + 独立事务，与共享作用域隔离
        services.AddScoped<IUnitOfWork>(sp =>
            new UnitOfWork(sp.GetRequiredService<ISqlSugarFactory>().CreateClient(options)));

        services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
        return services;
    }
}
