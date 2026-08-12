using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Station.Desktop.Infrastructure;

/// <summary>注册桌面端基础设施（设备驱动、持久化、加密、上传等，M3 起填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
