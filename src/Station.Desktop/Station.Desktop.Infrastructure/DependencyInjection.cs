using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Infrastructure;

namespace Station.Desktop.Infrastructure;

/// <summary>注册桌面端基础设施（设备驱动、持久化、加密、上传等，逐里程碑填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStationDatabase(configuration);
        services.AddHostedService<StationDbInitializerHostedService>();
        services.AddHostedService<UploadWorkerHostedService>();
        services.AddHostedService<PlatformSyncWorkerHostedService>();
        return services;
    }
}
