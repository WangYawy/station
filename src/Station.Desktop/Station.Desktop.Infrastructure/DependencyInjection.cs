using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Desktop.Infrastructure.Recorders;
using Station.Desktop.Infrastructure.Settings;
using Station.Infrastructure;
using Station.Infrastructure.Recorders;
using Station.Application.Settings;
using Station.Infrastructure.Collecting;
using Station.Application.Recorders;
using Station.Domain.Collecting;

namespace Station.Desktop.Infrastructure;

/// <summary>注册桌面端基础设施（设备驱动、持久化、加密、上传等，逐里程碑填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStationDatabase(configuration);

        services.AddSingleton<IRuntimeSettingsFile, RuntimeSettingsFile>();

        services.AddHostedService<StationDbInitializerHostedService>();
        services.AddHostedService<UploadWorkerHostedService>();
        services.AddHostedService<PlatformSyncWorkerHostedService>();
        services.AddHostedService<LedgerAndReportWorkerHostedService>();
        services.AddHostedService<LocalBackupWorkerHostedService>();
        services.AddHostedService<CacheCleanupWorkerHostedService>();
        services.AddHostedService<ScheduledCollectWorkerHostedService>();
        return services;
    }
}
