using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Security;
using Station.Crypto;
using Station.Crypto.Engine.Internal;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Security;

/// <summary>加密相关 DI 注册。</summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddStationSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        // 配置
        services.Configure<CryptoOptions>(configuration.GetSection(CryptoOptions.SectionName));

        // 内存缓存
        services.AddMemoryCache();

        // ---- 算法注册表（单例，无状态） ----
        services.AddSingleton<ICryptoAlgorithmRegistry>(AlgorithmRegistry.Default);

        // ---- 密钥（internal，Singleton） ----
        services.AddSingleton<PemKeyCache>();
        services.AddSingleton<MasterKeyProvider>();
        services.AddSingleton<IMasterKeyProvider>(sp =>
            sp.GetRequiredService<MasterKeyProvider>());

        // ---- 策略服务（Singleton：内部有缓存） ----
        services.AddSingleton<ICryptoPolicyService, CryptoPolicyService>();

        // ---- 门面（Scoped：依赖数据库） ----
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IFileCryptoService, FileCryptoService>();
        services.AddScoped<ILicenseCryptoService, LicenseCryptoService>();

        return services;
    }
}
