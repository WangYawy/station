using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Desktop.Application;
using Station.Desktop.Infrastructure;
using Station.Desktop.WebHost;

namespace Station.Desktop.Bootstrapper;

/// <summary>桌面端应用宿主组装：共享配置、基础设施、应用服务与内置 Web。</summary>
public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[]? args = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                configuration.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplicationServices(context.Configuration);
                // 基础设施在应用服务之后注册：采集源（MTP/UMS/模拟）与根文件存取（绑定文件）以桌面端覆盖为准
                services.AddInfrastructure(context.Configuration);
            })
            .UseWebHostModule(configuration);
    }
}
