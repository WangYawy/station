using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Station.Application;
using Station.Application;
using Station.Application.Settings;
using Station.Desktop.Infrastructure;
using Station.Desktop.Infrastructure.Settings;
using Station.Desktop.Services;
using Station.Desktop.Services.Kiosk;
using Station.Infrastructure;

namespace Station.Desktop.Bootstrapper;

/// <summary>桌面端应用宿主组装：共享配置、基础设施、应用服务与内置 Web。</summary>
public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[]? args = null)
    {
        // 1. 外部构建配置
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(RuntimeSettingsFile.ResolvePath(), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // 2. 创建主机
        return Host.CreateDefaultBuilder(args)
             .ConfigureAppConfiguration((_, config) =>
             {
                 // 3. 主机也加载相同的配置源（确保 Serilog 能读到完整配置）
                 //    注：Host.CreateDefaultBuilder 默认已加载 appsettings.json 和环境变量，
                 //    但显式添加可控制 reloadOnChange 和自定义文件顺序。
                 config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                 // 管理员在设置页/桌面端修改的配置：覆盖 appsettings.json，重启后生效
                 config.AddJsonFile(RuntimeSettingsFile.ResolvePath(), optional: true, reloadOnChange: true);
                 config.AddEnvironmentVariables();
             })
            .UseSerilog(
                (context, _, loggerConfig) =>
                {
                    // 4. 从主机的配置中读取 Serilog 设置
                    var rawConfig = context.Configuration;
                    var resolvedConfig = ResolveSerilogFilePaths(rawConfig); // 转换路径并创建目录

                    loggerConfig.ReadFrom.Configuration(resolvedConfig)
                                .Enrich.FromLogContext();
                },
                writeToProviders: true)
            .ConfigureServices((context, services) =>
            {
                // 应用层服务注册
                services.AddStationApplication(context.Configuration);
                // 基础设施在应用服务之后注册：采集源（MTP/UMS/模拟）与根文件存取（绑定文件）以桌面端覆盖为准
                services.AddStatoinInfrastructure(context.Configuration);

                // 桌面注册
                services.AddSingleton<IKioskGuard>(_ => KioskGuardFactory.Create());
            })
            //.UseWebHostModule(configuration); // 传入外部配置
            ;
    }

    /// <summary>
    /// 将 Serilog 配置中所有 File Sink 的 path 从相对路径转换为绝对路径，
    /// 根目录为 StationPaths.DataDirectory。
    /// </summary>
    private static IConfiguration ResolveSerilogFilePaths(IConfiguration config)
    {
        // 复制所有配置项到内存字典
        var dict = new Dictionary<string, string>();
        foreach (var kv in config.AsEnumerable())
        {
            dict[kv.Key] = kv.Value;
        }

        // 找出所有以 ":Args:path" 结尾的键（即 File Sink 的路径配置）
        var pathKeys = dict.Keys
            .Where(k => k.EndsWith(":Args:path", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in pathKeys)
        {
            var originalPath = dict[key];
            if (string.IsNullOrEmpty(originalPath))
                continue;

            string absolutePath;
            // 如果不是绝对路径，则拼接 DataDirectory
            if (!Path.IsPathRooted(originalPath))
            {
                absolutePath = Path.Combine(StationPaths.DataDirectory, originalPath);
            }
            // 若已是绝对路径，则保持不变（运维可直接指定）
            else
            {
                absolutePath = originalPath;
            }
            dict[key] = absolutePath;
            // 自动创建该路径的父目录
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            break;
        }

        // 用修改后的字典构建新配置
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(dict);
        return builder.Build();
    }
}
