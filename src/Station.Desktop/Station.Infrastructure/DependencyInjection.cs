using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application;
using Station.Application.Accounts;
using Station.Application.Alerts;
using Station.Application.Auditing;
using Station.Application.Collecting;
using Station.Application.Collecting.EventHandlers;
using Station.Application.Departments;
using Station.Application.DeviceDetection;
using Station.Application.Licensing;
using Station.Application.Notifications;
using Station.Application.Permissions;
using Station.Application.Recorders;
using Station.Application.Roles;
using Station.Application.Settings;
using Station.Application.Users;
using Station.Data.Abstractions;
using Station.Data.SqlSugar;
using Station.Domain.Abstractions;
using Station.Domain.Accounts;
using Station.Domain.Alerts;
using Station.Domain.Auditing;
using Station.Domain.Clocks;
using Station.Domain.Collecting;
using Station.Domain.Collecting.Events;
using Station.Domain.Cryptography;
using Station.Domain.Departments;
using Station.Domain.Licensing;
using Station.Domain.Outbox;
using Station.Domain.Permissions;
using Station.Domain.Recorders;
using Station.Domain.Roles;
using Station.Domain.Settings;
using Station.Domain.Stations;
using Station.Domain.UploadedFiles;
using Station.Domain.Users;
using Station.Infrastructure.Collecting;
using Station.Infrastructure.DeviceDetection;
using Station.Infrastructure.Events;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Persistence.Accounts;
using Station.Infrastructure.Persistence.Alerts;
using Station.Infrastructure.Persistence.Auditing;
using Station.Infrastructure.Persistence.Clocks;
using Station.Infrastructure.Persistence.Collecting;
using Station.Infrastructure.Persistence.Cryptography;
using Station.Infrastructure.Persistence.Departments;
using Station.Infrastructure.Persistence.Licensing;
using Station.Infrastructure.Persistence.Outbox;
using Station.Infrastructure.Persistence.Permissions;
using Station.Infrastructure.Persistence.Recorders;
using Station.Infrastructure.Persistence.Roles;
using Station.Infrastructure.Persistence.Settings;
using Station.Infrastructure.Persistence.Stations;
using Station.Infrastructure.Persistence.UploadedFiles;
using Station.Infrastructure.Persistence.Users;
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

        #region 数据服务
        // ---- 账号 ----
        services.AddScoped<IAccountRepository, AccountRepository>(); // 写，需要Domain
        services.AddScoped<IAccountLoopRepository, AccountLoopRepository>(); // 长连接
        services.AddScoped<IAccountQueryService, AccountQueryService>(); // 读，仅映射Dto

        // ---- 部门 ----
        services.AddScoped<IDeptRepository, DeptRepository>();
        services.AddScoped<IDeptSyncRepository, DeptSyncRepository>();
        services.AddScoped<IDeptQueryService, DeptQueryService>();

        // ---- 权限 ----
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPermissionQueryService, PermissionQueryService>();

        // ---- 角色 ----
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddScoped<IRoleSyncRepository, RoleSyncRepository>();
        services.AddScoped<IRoleQueryService, RoleQueryService>();

        // ---- 用户 ----
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IUserSyncRepository, UserSyncRepository>();
        services.AddScoped<IUserQueryService, UserQueryService>();

        // ---- 采集任务 ----
        services.AddScoped<ICollectTaskRepository, CollectTaskRepository>();
        services.AddScoped<ICollectTaskSyncRepository, CollectTaskSyncRepository>();
        services.AddScoped<ICollectTaskQueryService, CollectTaskQueryService>();

        services.AddScoped<ICollectFileRepository, CollectFileRepository>();
        services.AddScoped<ICollectFileSyncRepository, CollectFileSyncRepository>();
        services.AddScoped<ICollectFileQueryService, CollectFileQueryService>();

        // ---- 记录仪 ----
        services.AddScoped<IRecorderRepository, RecorderRepository>();
        services.AddScoped<IRecorderSyncRepository, RecorderSyncRepository>();
        services.AddScoped<IRecorderQueryService, RecorderQueryService>();

        // ---- 报警 ----
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IAlertQueryService, AlertQueryService>();

        // ---- 审计日志 ----
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        // ----- 授权激活 ----
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<ILicenseSyncRepository, LicenseSyncRepository>();

        // ---- Outbox ----
        services.AddScoped<ISyncOutboxRepository, SyncOutboxRepository>();
        services.AddScoped<ISyncOutboxSyncRepository, SyncOutboxSyncRepository>();

        // ---- 采集上传文件台账 ----
        services.AddScoped<IUploadedFileRepository, UploadedFileRepository>();

        // 其他
        services.AddScoped<IStationInfoRepository, StationInfoRepository>();
        services.AddScoped<IClockStateRepository, ClockStateRepository>();
        services.AddScoped<ISysCryptoPolicyRepository, SysCryptoPolicyRepository>();
        services.AddScoped<ISysSettingRepository, SysSettingRepository>();

        #region // 领域事件
        // ProgressAggregator：单例，同时作为 IHostedService 和领域事件处理器
        services.AddSingleton<ProgressAggregator>();
        services.AddHostedService(sp => sp.GetRequiredService<ProgressAggregator>());
        services.AddSingleton<IDomainEventHandler<CollectFileCollected>>(
            sp => sp.GetRequiredService<ProgressAggregator>());

        // 审计
        services.AddSingleton<IDomainEventHandler<CollectTaskStatusChanged>,
                              AuditOnCollectTaskStatusChanged>();
        services.AddSingleton<IDomainEventHandler<CollectFileUploaded>,
                              AuditOnCollectFileUploaded>();

        // 平台同步
        services.AddSingleton<IDomainEventHandler<CollectTaskCompleted>,
                              SyncOutboxOnCollectTaskCompleted>();
        // ui 事件
        services.AddScoped<IUiNotificationService, UiNotificationService>();
        #endregion
        #endregion

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
            else if (OperatingSystem.IsLinux())
                return new LinuxMachineFingerprintProvider();
            else return null!;
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

        services.AddSingleton<ICollectTaskNoGenerator, CollectTaskNoGenerator>();
        services.AddSingleton<ICollectFileNoGenerator, CollectFileNoGenerator>();
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

        // 采集站信息
        services.AddSingleton<IStationInfoService, StationInfoService>();

        // 事件总线
        // ---- 配置 ----
        services.Configure<DomainEventBusOptions>(
            configuration.GetSection(DomainEventBusOptions.SectionName));

        // ---- 事件总线实现 ----
        services.AddSingleton<SyncDomainEventBus>();
        services.AddSingleton<ChannelDomainEventBus>();
        services.AddSingleton<InMemoryEventStore>();

        // 按配置切换
        services.AddSingleton<IDomainEventBus>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<DomainEventBusOptions>>().Value;
            return opt.Mode switch
            {
                DomainEventDispatchMode.Channel => sp.GetRequiredService<ChannelDomainEventBus>(),
                _ => sp.GetRequiredService<SyncDomainEventBus>()
            };
        });

        // 后台 Dispatcher（仅 Channel 模式注册）
        services.AddHostedService<ChannelEventDispatcher>();

        return services;
    }
}
