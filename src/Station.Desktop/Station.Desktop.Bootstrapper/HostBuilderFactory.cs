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
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);
                services.AddApplicationServices(context.Configuration);
            })
            .UseWebHostModule();
    }
}
