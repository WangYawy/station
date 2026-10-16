using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Station.Data;
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

namespace Station.Infrastructure;

/// <summary>应用启动时初始化业务表结构与认证种子数据（幂等）。</summary>
public sealed class StationDbInitializerHostedService : IHostedService
{
    private readonly IAuthSeeder _authSeeder;
    private readonly IDatabaseInitializer _initializer;
    private readonly ILogger<StationDbInitializerHostedService> _logger;

    public StationDbInitializerHostedService(
        IAuthSeeder authSeeder,
        IDatabaseInitializer initializer,
        ILogger<StationDbInitializerHostedService> logger)
    {
        _authSeeder = authSeeder;
        _initializer = initializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _initializer.EnsureCreated(
            typeof(StationInfoPo),
            typeof(ClockStatePo),
            typeof(SysCryptoPolicyPo),
            typeof(SysSettingPo),
            typeof(LicensePo),
            typeof(SyncOutboxPo),
            typeof(UploadedFilePo),
            typeof(CollectTaskPo),
            typeof(CollectFilePo),
            typeof(DeptPo),
            typeof(PermissionPo),
            typeof(RolePo),
            typeof(RolePermissionPo),
            typeof(UserPo),
            typeof(UserRolePo),
            typeof(AccountPo),
            typeof(AuditLogPo),
            typeof(AuditLogArchivePo),
            typeof(AlertPo),
            typeof(RecorderPo));
        // 存量库补列：紧急优先标记（新库由 CodeFirst 自动创建）
        //_initializer.EnsureColumn("station_collect_task", "IsEmergency", "int");
        //// 存量库补列：设备根路径（多设备/混合协议按任务路由）
        //_initializer.EnsureColumn("station_collect_task", "SourceRoot", "varchar(512)");
        await _authSeeder.EnsureAsync();
        _logger.LogInformation("数据库初始化完成（建表/补列/认证种子）");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
