using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Desktop.Application.Settings;
using Station.Application.Settings;

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

        // 系统设置（单机版）：基本/存储/采集热应用 + 运行时文件持久化；设备自检
        var basicSection = configuration.GetSection(StationOptions.SectionName);
        services.Configure<StationOptions>(basicSection);
        services.AddSingleton(basicSection.Get<StationOptions>() ?? new StationOptions());
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<ISystemSelfCheckService, SystemSelfCheckService>();
        return services;
    }
}
