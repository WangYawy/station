using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Desktop.Infrastructure.Collecting;
using Station.Desktop.Infrastructure.Recorders;
using Station.Desktop.Infrastructure.Settings;
using Station.Infrastructure;
using Station.Infrastructure.Recorders;
using Station.Application.Settings;

namespace Station.Desktop.Infrastructure;

/// <summary>注册桌面端基础设施（设备驱动、持久化、加密、上传等，逐里程碑填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStationDatabase(configuration);

        services.AddSingleton<IRuntimeSettingsFile, RuntimeSettingsFile>();

        // MTP 采集源（仅 Windows；Linux 上选择 mtp 时给出明确错误）
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<MtpCollectSource>();
            services.AddSingleton<IRecorderRootFileStore>(sp =>
                new CompositeRecorderRootFileStore(sp.GetRequiredService<MtpCollectSource>()));
        }
        else
        {
            services.AddSingleton<IRecorderRootFileStore, FileSystemRecorderRootFileStore>();
        }
        services.AddSingleton<ICollectSource>(sp =>
        {
            var collect = sp.GetRequiredService<CollectOptions>();
            return collect.SourceMode switch
            {
                "mtp" when OperatingSystem.IsWindows() => sp.GetRequiredService<MtpCollectSource>(),
                "mtp" => throw new PlatformNotSupportedException("MTP 采集源仅支持 Windows"),
                "ums" => new UmsCollectSource(collect),
                _ => new SimulatedCollectSource(collect)
            };
        });

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
