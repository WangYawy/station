using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;

namespace Station.Desktop.Application;

/// <summary>注册桌面端应用服务（共享应用层：认证/RBAC/数据权限/审计等，逐里程碑扩展）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddStationApplication(configuration);
        services.AddSingleton<ISessionManager, SessionManager>();
        services.Configure<OperationAuthOptions>(configuration.GetSection(OperationAuthOptions.SectionName));
        services.AddSingleton<IOperationAccessService, OperationAccessService>();
        return services;
    }
}
