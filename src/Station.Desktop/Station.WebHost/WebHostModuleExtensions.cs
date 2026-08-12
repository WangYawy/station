using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Station.WebHost;

/// <summary>将单机版内置 Web 宿主挂载到桌面端 Generic Host。</summary>
public static class WebHostModuleExtensions
{
    public static IHostBuilder UseWebHostModule(this IHostBuilder builder)
    {
        return builder.ConfigureWebHostDefaults(web =>
        {
            web.UseStartup<Startup>();
        });
    }
}
