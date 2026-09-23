using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Infrastructure.Security.Keys;
using Station.Infrastructure.Security.Policy;
using Station.Infrastructure.Security.Providers.Encryptors;
using Station.Infrastructure.Security.Providers.Hashers;
using Station.Infrastructure.Security.Providers.Macs;
using Station.Infrastructure.Security.Providers.PasswordHashers;
using Station.Infrastructure.Security.Providers.Signers;
using Station.Infrastructure.Settings;

namespace Station.Infrastructure.Security;

/// <summary>加密相关服务注册。</summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddStationSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        // 绑定配置节
        services.Configure<CryptoOptions>(
            configuration.GetSection(CryptoOptions.SectionName));

        // 主密钥提供者（单例：内部有文件锁与缓存）
        services.AddSingleton<IMasterKeyProvider, MasterKeyProvider>();

        // 算法 Provider（单例、无状态）
        services.AddSingleton<IPasswordHasher, Sm3Pbkdf2Hasher>();
        services.AddSingleton<IPasswordHasher, Md5LegacyHasher>();
        services.AddSingleton<IPasswordHasher, Pbkdf2Sha256Hasher>();
        services.AddSingleton<ISigner, Sm2Sm3Signer>();
        services.AddSingleton<ISigner, RsaSha256Signer>();
        services.AddSingleton<IEncryptor, Sm4GcmEncryptor>();
        services.AddSingleton<IEncryptor, AesGcmEncryptor>();
        services.AddSingleton<IHasher, Sm3Hasher>();
        services.AddSingleton<IHasher, Sha256Hasher>();
        services.AddSingleton<IMacProvider, HmacSm3Provider>();
        services.AddSingleton<IMacProvider, HmacSha256Provider>();

        // 工厂 + 策略 + 轮换
        services.AddSingleton<ICryptoProviderFactory, CryptoProviderFactory>();
        services.AddSingleton<ICryptoPolicyService, CryptoPolicyService>();
        services.AddScoped<IKeyRotationService, KeyRotationService>();

        // 密码服务门面（Scoped，与 ICryptoPolicyService 生命周期一致）
        services.AddSingleton<IPasswordService, PasswordService>();

        // 文件加解密
        services.AddScoped<IFileEncryptionService, FileEncryptionService>();

        // 文件摘要 + 元数据签名
        services.AddScoped<IFileSigningService, FileSigningService>();

        services.AddScoped<IReportingSigner, ReportingSigner>();
        // Binding 密钥提供者
        services.AddSingleton<IBindingSecretProvider, BindingSecretProvider>();

        return services;
    }
}
