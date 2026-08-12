using Microsoft.Extensions.DependencyInjection;

namespace Station.Application;

/// <summary>注册桌面端应用服务（M3 起按模块填充）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        return services;
    }
}
