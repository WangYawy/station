using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Desktop.Application;
using Station.Desktop.Infrastructure;
using Station.Desktop.WebHost;
using Station.Desktop.Infrastructure.Settings;
using Serilog;
using Station.Infrastructure;

namespace Station.Desktop.Bootstrapper;

/// <summary>桌面端应用宿主组装：共享配置、基础设施、应用服务与内置 Web。</summary>
public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[]? args = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(RuntimeSettingsFile.ResolvePath(), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var logDirectory = Path.Combine(StationPaths.DataDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "desktop-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Information("Serilog 初始化完成（桌面端），日志目录 {LogDirectory}", logDirectory);

        return Host.CreateDefaultBuilder(args)
            .UseSerilog(
                (context, _, configuration) =>
                    configuration
                        .ReadFrom.Configuration(context.Configuration)
                        .Enrich.FromLogContext()
                        .WriteTo.Console()
                        .WriteTo.File(
                            Path.Combine(logDirectory, "desktop-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 30,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"),
                writeToProviders: true)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                // 管理员在设置页/桌面端修改的配置：覆盖 appsettings.json，重启后生效
                configuration.AddJsonFile(RuntimeSettingsFile.ResolvePath(), optional: true, reloadOnChange: true);
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
