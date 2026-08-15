using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Infrastructure.Backup;
using Station.Infrastructure.Db;
using Station.Infrastructure.Licensing;
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
        // 默认 SQLite 相对路径重定位到应用数据目录（安装目录只读，避免 Program Files//usr 下不可写）
        if (options.Provider == DbProvider.Sqlite)
        {
            options.ConnectionString = StationPaths.RebaseSqliteConnectionString(options.ConnectionString);
            Directory.CreateDirectory(StationPaths.DataDirectory);
        }

        services.AddSingleton(Options.Create(options));
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
        services.AddSingleton<IMachineFingerprintProvider>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsMachineFingerprintProvider()
                : new LinuxMachineFingerprintProvider());
        services.AddSingleton<ISm4KeyProvider>(_ => new Sm4KeyProvider());

        // UnitOfWork：独立客户端 + 独立事务，与共享作用域隔离
        services.AddScoped<IUnitOfWork>(sp =>
            new UnitOfWork(sp.GetRequiredService<ISqlSugarFactory>().CreateClient(options)));

        var backupSection = configuration.GetSection(BackupOptions.SectionName);
        services.Configure<BackupOptions>(backupSection);
        services.AddSingleton(backupSection.Get<BackupOptions>() ?? new BackupOptions());
        services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
        services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
        return services;
    }
}
