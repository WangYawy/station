using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace Station.Desktop.WebHost;

/// <summary>将单机版内置 Web 宿主挂载到桌面端 Generic Host。</summary>
public static class WebHostModuleExtensions
{
    public static IHostBuilder UseWebHostModule(this IHostBuilder builder, IConfiguration? configuration = null)
    {
        return builder.ConfigureWebHostDefaults(web =>
        {
            var options = (configuration ?? new ConfigurationBuilder().Build())
                .GetSection(WebOptions.SectionName)
                .Get<WebOptions>() ?? new WebOptions();
            var address = options.EnableLan ? "0.0.0.0" : options.ListenAddress;
            var urls = new List<string> { $"http://{address}:{options.Port}" };
            if (options.EnableLan && options.EnableHttps)
            {
                urls.Add($"https://{address}:{options.HttpsPort}");
            }

            web.UseUrls(urls.ToArray());
            web.ConfigureKestrel(kestrel =>
            {
                if (!options.EnableLan || !options.EnableHttps ||
                    string.IsNullOrWhiteSpace(options.CertificatePath))
                {
                    return;
                }

                kestrel.ConfigureHttpsDefaults(https =>
                {
                    https.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        options.CertificatePath,
                        options.CertificatePassword);
                });
            });
            web.UseStartup<Startup>();
        });
    }
}
