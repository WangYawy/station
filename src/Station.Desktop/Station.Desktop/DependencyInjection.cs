using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Diagnostics;
using Station.Infrastructure.Diagnostics;

namespace Station.Desktop;
public static class DependencyInjection
{
    /// <summary>
    /// 注册数据访问层：配置节 <c>Station:Db</c>（Provider + ConnectionString）。
    /// 提供 ISqlSugarClient（单例作用域）、IDbDialect、IDatabaseInitializer、IUnitOfWork、IRepository&lt;&gt;。
    /// </summary>
    public static IServiceCollection AddStatoinDesktop(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 屏蔽，只有shellwindow用
        // services.AddSingleton<IKioskGuard>(_ => KioskGuardFactory.Create());

        // 启动自检
        services.AddSingleton<StartupSelfCheckService>();
        services.AddSingleton<IStartupSelfCheckService>(sp => sp.GetRequiredService<StartupSelfCheckService>());

        return services;
    }
}
