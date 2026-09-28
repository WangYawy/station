using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.DeviceDetection;
using Station.Application.Licensing;
using Station.Application.Settings;
using Station.Data;
using Station.Data.Abstractions;
using Station.Data.SqlSugar;
using Station.Desktop.Infrastructure;
using Station.Desktop.Infrastructure.Settings;
using Station.Domain.Collecting;
using Station.Infrastructure.Collecting;
using Station.Infrastructure.DeviceDetection;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Security;
using Station.Infrastructure.Settings;
using Station.Infrastructure.Storage;

namespace Station.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册数据访问层：配置节 <c>Station:Db</c>（Provider + ConnectionString）。
    /// 提供 ISqlSugarClient（单例作用域）、IDbDialect、IDatabaseInitializer、IUnitOfWork、IRepository&lt;&gt;。
    /// </summary>
    public static IServiceCollection AddStatoinInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ==========================================
        // 1. 基础日志 & 路径初始化
        // ==========================================
        services.AddLogging();
        #region 数据库依赖
        // ==========================================
        // 2. 数据库注册块
        // ==========================================
        //var section = configuration.GetSection(DbOptions.SectionName);
        //var options = section.Get<DbOptions>() ?? new DbOptions();

        services.AddStationDataSqlSugar(configuration, db =>
        {
            // 产品侧专属：SQLite 相对路径重定位到应用数据目录
            if (db.Provider == DbProvider.Sqlite)
            {
                db.ConnectionString = StationPaths.RebaseSqliteConnectionString(db.ConnectionString);
                Directory.CreateDirectory(StationPaths.DataDirectory);
            }
        });
        // Auth 种子
        services.AddSingleton<IAuthSeeder, AuthSeeder>();
        #endregion
        // ==========================================
        // 3. 机器硬件指纹（跨平台）
        // ==========================================
        services.AddSingleton<IMachineFingerprintProvider>(sp =>
        {
            if (OperatingSystem.IsWindows())
                return new WindowsMachineFingerprintProvider();
            return new LinuxMachineFingerprintProvider();
        });
        // ==========================================
        // 4. 加密 & 秘钥
        // ==========================================
        services.AddStationSecurity(configuration);

        // ==========================================
        // 5. 记录仪设备文件采集器
        // MTP 需要根据不同平台引入包，在Desktop.Infrastructure中依赖注入
        // ==========================================
        // 1. 注册所有具体采集源实现
        services.AddSingleton<UmsCollectSource>(); // ums采集源
        services.AddSingleton<SimulatedCollectSource>(); // 模拟采集源
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<WPDMtpCollectSource>(); // mtp windows采集源
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<LinuxMtpCollectSource>(); // mtp linux采集源
        }
        // 2. 注册采集源提供者（动态路由）
        services.AddSingleton<ICollectSourceProvider, CollectSourceProvider>();
        // ==========================================
        // 6. 记录仪设备检测器
        // MTP 需要根据不同平台引入包，在Desktop.Infrastructure中依赖注入
        // ==========================================
        services.AddSingleton<IRecorderDeviceDetector, UmsDeviceDetector>(); // ums 设备检测器
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IRecorderDeviceDetector, WPDMtpDeviceDetector>(); // windows mtp设备检测器
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IRecorderDeviceDetector, LinuxMtpDeviceDetector>(); // linux mtp设备检测器
        }
        // 注册具体类为单例
        services.AddSingleton<RecorderConnectMonitor>();
        // 注册接口（指向同一个实例）
        services.AddSingleton<IDevicePresenceService>(sp => sp.GetRequiredService<RecorderConnectMonitor>());
        // 注册后台托管服务（复用同一个实例）
        services.AddHostedService(sp => sp.GetRequiredService<RecorderConnectMonitor>());
        // ==========================================
        // 7. 文件存储器（包含多目标）
        // ==========================================
        services.AddStationStorage(configuration);
        // ==========================================
        // 8. 后台运行服务
        // ==========================================
        services.AddHostedService<StationDbInitializerHostedService>();
        services.AddHostedService<UploadWorkerHostedService>();
        services.AddHostedService<PlatformSyncWorkerHostedService>();
        services.AddHostedService<LedgerAndReportWorkerHostedService>();
        services.AddHostedService<LocalBackupWorkerHostedService>();
        services.AddHostedService<CacheCleanupWorkerHostedService>();
        services.AddHostedService<ScheduledCollectWorkerHostedService>();

        // 运行时配置文件
        services.AddSingleton<IRuntimeSettingsFile, RuntimeSettingsFile>();

        // 内存缓存
        services.AddMemoryCache();

        // 配置持久化
        services.AddScoped<ISettingStore, SettingStore>();

        return services;
    }
}
