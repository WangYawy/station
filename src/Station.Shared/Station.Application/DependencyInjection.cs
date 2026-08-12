using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Authorization;
using Station.Application.Collecting;
using Station.Application.Users;

namespace Station.Application;

public static class DependencyInjection
{
    /// <summary>
    /// 注册应用服务：认证（登录/锁定/改密）、授权（会话/权限）、数据权限、用户/部门/角色管理、审计日志。
    /// 配置节：<c>Station:Auth</c>（登录策略）。
    /// </summary>
    public static IServiceCollection AddStationApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authSection = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(authSection);
        services.AddSingleton(authSection.Get<AuthOptions>() ?? new AuthOptions());

        var collectSection = configuration.GetSection(CollectOptions.SectionName);
        services.Configure<CollectOptions>(collectSection);
        services.AddSingleton(collectSection.Get<CollectOptions>() ?? new CollectOptions());
        services.AddSingleton<ICollectSource, SimulatedCollectSource>();
        services.AddScoped<ICollectTaskService, CollectTaskService>();

        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IDataScopeProvider, DataScopeProvider>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
