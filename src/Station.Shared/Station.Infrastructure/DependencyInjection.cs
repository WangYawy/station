using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.IdGenerators;
using Station.Application.Recorders;
using Station.Domain.Security;
using Station.Application.Services;
using Station.Application.Storage;
using Station.Domain;
using Station.Domain.Repositories;
using Station.Infrastructure.Backup;
using Station.Infrastructure.Collecting;
using Station.Infrastructure.Db;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Recorders;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Infrastructure.Storage;
using static Org.BouncyCastle.Math.EC.ECCurve;

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
        // ==========================================
        // 1. 基础日志 & 路径初始化
        // ==========================================
        services.AddLogging();
        #region 数据库依赖

        // ==========================================
        // 2. 雪花算法配置（单例静态初始化）
        // ==========================================
        var snowflakeSection = configuration.GetSection(SnowFlakeOptions.SectionName);
        var snowflake = snowflakeSection.Get<SnowFlakeOptions>() ?? new SnowFlakeOptions();
        SnowFlakeSingle.DatacenterId = snowflake.DatacenterId;
        SnowFlakeSingle.WorkId = snowflake.WorkId;

        // ==========================================
        // 3. 数据库连接配置（DbOptions）
        // ==========================================
        var section = configuration.GetSection(DbOptions.SectionName);
        var options = section.Get<DbOptions>() ?? new DbOptions();
        // 默认 SQLite 相对路径重定位到应用数据目录（安装目录只读，避免 Program Files//usr 下不可写）
        if (options.Provider == DbProvider.Sqlite)
        {
            options.ConnectionString = StationPaths.RebaseSqliteConnectionString(options.ConnectionString);
            Directory.CreateDirectory(StationPaths.DataDirectory);
        }
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(options); // 直接注入 POCO，方便工厂使用

        // ==========================================
        // 4. 注册 SqlSugar 工厂（单例）
        // ==========================================
        services.AddSingleton<ISqlSugarFactory, SqlSugarFactory>();

        // ==========================================
        // 5. 注册“短连接”客户端（供 IRepository 使用）
        //    使用 CreateScope（SQLite 常驻，其他自动关闭），线程安全共享。
        //    生命周期：Singleton（单例）
        // ==========================================
        services.AddSingleton<ISqlSugarClient>(sp =>
        {
            var factory = sp.GetRequiredService<ISqlSugarFactory>();
            var options = sp.GetRequiredService<DbOptions>();
            return factory.CreateScope(options); // 内部决定 autoClose
        });
        services.AddSingleton<ISqlSugarClient>(sp =>
            sp.GetRequiredService<ISqlSugarFactory>().CreateScope(options)); // 内部会根据 Provider 决定 autoClose 策略
        // ==========================================
        // 6. 注册“长连接”客户端（供 ILoopRepository 使用
        //    使用 CreateClient(autoCloseConnection: false)，保证连接不自动释放。
        //    生命周期：Scoped（作用域），确保同一 Scope 内所有仓储共享此实例。
        //    绑定到标记接口 ILoopSqlSugarClient，避免与短连接冲突。
        // ==========================================
        services.AddScoped<ILoopSqlSugarClient>(sp =>
        {
            var factory = sp.GetRequiredService<ISqlSugarFactory>();
            var options = sp.GetRequiredService<DbOptions>();
            // 使用工厂公开的 BuildConfig 方法，强制 autoCloseConnection: false
            var config = factory.BuildConfig(options, autoCloseConnection: false);
            // 返回自定义的长连接客户端
            return new LoopSqlSugarClient(config);
        });
        // ==========================================
        // 7. 注册数据库方言 & 初始化器
        // ==========================================
        services.AddSingleton<IDbDialect>(_ => DbDialectFactory.Create(options.Provider));
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        // ==========================================
        // 8. 注册 ID 生成器 & 权限种子
        // ==========================================
        services.AddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.Configure<AuthSeedOptions>(configuration.GetSection("Station:Auth"));
        services.AddScoped<IAuthSeeder, AuthSeeder>();
        // ==========================================
        // 9. 注册 UnitOfWork（独立短连接事务，与共享短连接隔离）
        // ==========================================
        services.AddScoped<IUnitOfWork>(sp =>
            new UnitOfWork(sp.GetRequiredService<ISqlSugarFactory>().CreateClient(options)));
        // ==========================================
        // 10. 注册备份服务 & 健康检查
        // ==========================================
        var backupSection = configuration.GetSection(BackupOptions.SectionName);
        services.Configure<BackupOptions>(backupSection);
        services.AddSingleton(backupSection.Get<BackupOptions>() ?? new BackupOptions());
        services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
        services.AddSingleton<IDatabaseHealthService, DatabaseHealthService>();
        // ==========================================
        // 11. 注册仓储（核心）
        //     IRepository -> 注入短连接 ISqlSugarClient（Singleton）
        //     ILoopRepository -> 注入长连接 ILoopSqlSugarClient（Scoped）
        // ==========================================
        services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
        services.AddScoped(typeof(ILoopRepository<>), typeof(LoopRepositoryBase<>));

        #endregion
        // ==========================================
        // 机器硬件指纹（跨平台）
        // ==========================================
        services.AddSingleton<IMachineFingerprintProvider>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsMachineFingerprintProvider()
                : new LinuxMachineFingerprintProvider());
        // ==========================================
        // 加密 & 秘钥
        // ==========================================
        services.AddSingleton<IPasswordHasher, Sm3PasswordHasher>();
        services.AddSingleton<IFileChecksumService, FileChecksumService>();
        services.AddSingleton<IFileEncryptionService, FileEncryptionService>();
        services.AddSingleton<ILicenseSignatureService, LicenseSignatureService>();
        services.AddSingleton<ISecretProtector, Sm4SecretProtector>(); // 适配器模式
        services.AddSingleton<ISm4KeyProvider, Sm4KeyProvider>(); // ISm4KeyProvider 的接口定义已移入 Application，但实现在这里注册
        services.AddSingleton<IHashService, Sm3HashService>();

        // ==========================================
        // 采集源（默认ums）
        // MTP 需要根据不同平台引入包，在Desktop.Infrastructure中依赖注入
        // ==========================================
        services.AddSingleton<UmsCollectSource>();
        services.AddSingleton<SimulatedCollectSource>();
        services.AddSingleton<ICollectSource>(sp =>
        {
            var collect = sp.GetRequiredService<CollectOptions>();
            return collect.SourceMode == "ums"
                ? sp.GetRequiredService<UmsCollectSource>()
                : sp.GetRequiredService<SimulatedCollectSource>();
        });
        services.AddSingleton<IRecorderRootFileStore, FileSystemRecorderRootFileStore>();
        services.AddSingleton<ICollectSourceProvider, DefaultCollectSourceProvider>();
        // ==========================================
        // 存储配置（包含多目标）
        // ==========================================
        var storageSection = configuration.GetSection(StorageOptions.SectionName);
        services.Configure<StorageOptions>(storageSection);
        // 将 StorageOptions 注册为 IStorageConfiguration（用于 Application 层）
        services.AddSingleton<IStorageConfiguration>(sp =>
            sp.GetRequiredService<IOptions<StorageOptions>>().Value);
        // 注册多个存储目标实例
        services.AddSingleton<IEnumerable<IStorageTarget>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var targets = new List<IStorageTarget>();
            var secretProtector = sp.GetRequiredService<ISecretProtector>();

            foreach (var targetConfig in options.Targets)
            {
                IStorageTarget target = targetConfig.Kind switch
                {
                    StorageTargetKind.Local => new LocalDiskStorageTarget(targetConfig, sp.GetRequiredService<IOptions<StorageOptions>>(), secretProtector),
                    StorageTargetKind.Ftp => new FtpStorageTarget(targetConfig, sp.GetRequiredService<IOptions<StorageOptions>>(), secretProtector),
                    StorageTargetKind.Sftp => new SftpStorageTarget(targetConfig, sp.GetRequiredService<IOptions<StorageOptions>>(), secretProtector),
                    _ => throw new NotSupportedException($"Unsupported storage target: {targetConfig.Kind}")
                };
                // 应用熔断装饰器（如果配置了阈值）
                var threshold = targetConfig.CircuitBreakerThreshold ?? options.CircuitBreakerThreshold;
                var cooldown = targetConfig.CircuitBreakerCooldownSeconds ?? options.CircuitBreakerCooldownSeconds;
                if (threshold > 0 && cooldown > 0)
                {
                    var breaker = new StorageCircuitBreaker(threshold, cooldown);
                    target = new CircuitBreakerStorageTarget(target, breaker);
                }

                targets.Add(target);
            }
            return targets;
        });
        services.AddScoped<IStorageService, StorageService>(); // 注册存储服务


        return services;
    }
}
