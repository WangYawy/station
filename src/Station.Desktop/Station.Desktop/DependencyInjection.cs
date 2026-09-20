using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Desktop.Services;
using Station.Desktop.Services.Kiosk;
using Station.Desktop.ViewModels;

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
        // 屏蔽
        services.AddSingleton<IKioskGuard>(_ => KioskGuardFactory.Create());

        return services;
    }
}
