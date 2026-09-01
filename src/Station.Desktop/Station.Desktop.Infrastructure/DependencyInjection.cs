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
using Station.Infrastructure.Collecting;
using Station.Application.Recorders;

namespace Station.Desktop.Infrastructure;

/// <summary>注册桌面端基础设施（设备驱动、持久化、加密、上传等，逐里程碑填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStationDatabase(configuration);

        services.AddSingleton<IRuntimeSettingsFile, RuntimeSettingsFile>();

        // MTP 采集源 覆盖共享层默认实现
        
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<MtpCollectSource>();
            services.AddSingleton<IRecorderRootFileStore>(sp =>
                new CompositeRecorderRootFileStore(sp.GetRequiredService<MtpCollectSource>()));
        }
        else
        {
           
            if (OperatingSystem.IsLinux())
            {
                // Linux 真实 MTP（libmtp）：与 UMS 一起参与"真实设备模式"混合路由
                services.AddSingleton<LinuxMtpCollectSource>();
                services.AddSingleton<IRecorderRootFileStore>(sp =>
                    new CompositeRecorderRootFileStore(sp.GetRequiredService<LinuxMtpCollectSource>()));
            }
        }

        // 按设备协议路由采集源（UMS/MTP 混合接入）：覆盖共享层默认实现
        services.AddSingleton<ICollectSourceProvider>(sp =>
        {
            var collect = sp.GetRequiredService<CollectOptions>();
            var ums = sp.GetRequiredService<UmsCollectSource>();
            var simulated = sp.GetRequiredService<SimulatedCollectSource>();
            if (OperatingSystem.IsWindows())
            {
                return new CollectSourceProvider(collect, ums, simulated, sp.GetRequiredService<MtpCollectSource>());
            }

            if (OperatingSystem.IsLinux())
            {
                return new CollectSourceProvider(collect, ums, simulated, mtpLinux: sp.GetRequiredService<LinuxMtpCollectSource>());
            }

            return new CollectSourceProvider(collect, ums, simulated);
        });

        // 记录仪接入监听：UMS/MTP 设备接入稳定后识别并自动采集（模拟源不工作）
        services.AddSingleton<IRecorderDeviceDetector, UmsDeviceDetector>();
        services.AddSingleton<IRecorderDeviceDetector, MtpDeviceDetector>();
        services.AddHostedService<RecorderConnectWatcherHostedService>();
        services.AddSingleton<ICollectSource>(sp =>
        {
            var collect = sp.GetRequiredService<CollectOptions>();
            return collect.SourceMode switch
            {
                "mtp" when OperatingSystem.IsWindows() => sp.GetRequiredService<MtpCollectSource>(),
                "mtp" when OperatingSystem.IsLinux() => sp.GetRequiredService<LinuxMtpCollectSource>(),
                "ums" => sp.GetRequiredService<UmsCollectSource>(),
                _ => sp.GetRequiredService<SimulatedCollectSource>()
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
