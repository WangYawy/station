using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Application.Alerts;
using Station.Application.Authentication;
using Station.Application.Authorization;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Recorders;
using Station.Application.Uploading;
using Station.Application.Users;
using Station.Infrastructure.Recorders;
using Station.Infrastructure.Storage;

namespace Station.Application;

public static class DependencyInjection
{
    /// <summary>
    /// 注册应用服务：认证（登录/锁定/改密）、授权（会话/权限）、数据权限、用户/部门/角色管理、审计日志。
    /// 配置节：<c>Station:Auth</c>（登录策略）。
    /// </summary>
    public static IServiceCollection AddStationApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authSection = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(authSection);
        services.AddSingleton(authSection.Get<AuthOptions>() ?? new AuthOptions());

        var collectSection = configuration.GetSection(CollectOptions.SectionName);
        services.Configure<CollectOptions>(collectSection);
        services.AddSingleton(collectSection.Get<CollectOptions>() ?? new CollectOptions());
        services.AddSingleton<ICollectSource, SimulatedCollectSource>();
        services.AddScoped<ICollectTaskService, CollectTaskService>();

        var bindingSection = configuration.GetSection(BindingOptions.SectionName);
        services.Configure<BindingOptions>(bindingSection);
        services.AddSingleton<RecorderBindingFile>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IRecorderService, RecorderService>();
        services.AddScoped<IRecorderIdentificationService, RecorderIdentificationService>();

        var storageSection = configuration.GetSection(StorageOptions.SectionName);
        services.Configure<StorageOptions>(storageSection);
        services.AddSingleton(storageSection.Get<StorageOptions>() ?? new StorageOptions());
        services.AddSingleton<IStorageCircuitBreaker>(sp =>
        {
            var storage = sp.GetRequiredService<StorageOptions>();
            return new StorageCircuitBreaker(storage.CircuitBreakerThreshold, storage.CircuitBreakerCooldownSeconds);
        });
        services.AddSingleton<IStorageTarget>(sp =>
        {
            var storage = sp.GetRequiredService<StorageOptions>();
            return storage.Target switch
            {
                StorageTargetKind.Ftp => new FtpStorageTarget(Options.Create(storage)),
                StorageTargetKind.Sftp => new SftpStorageTarget(Options.Create(storage)),
                _ => new LocalDiskStorageTarget(Options.Create(storage))
            };
        });
        services.AddScoped<IUploadService, UploadService>();

        var platformSection = configuration.GetSection(PlatformOptions.SectionName);
        services.Configure<PlatformOptions>(platformSection);
        services.AddSingleton(platformSection.Get<PlatformOptions>() ?? new PlatformOptions());
        services.AddHttpClient<IPlatformClient, HttpPlatformClient>();
        services.AddSingleton<IStationContext, StationContext>();
        services.AddSingleton<ICollectControl, CollectControl>();
        services.AddSingleton<IConfigSyncState, ConfigSyncState>();
        services.Configure<CommandVerifierOptions>(configuration.GetSection(CommandVerifierOptions.SectionName));
        services.AddScoped<ICommandSignatureVerifier, CommandSignatureVerifier>();
        services.Configure<ReportingOptions>(configuration.GetSection(ReportingOptions.SectionName));
        services.AddSingleton(configuration.GetSection(ReportingOptions.SectionName).Get<ReportingOptions>() ?? new ReportingOptions());
        services.AddScoped<ISyncOutboxService, SyncOutboxService>();
        services.AddScoped<ICommandExecutor, CommandExecutor>();
        services.AddScoped<ICommandService, CommandService>();
        services.AddScoped<IFileLedgerService, FileLedgerService>();
        services.AddScoped<IConfigApplyService, ConfigApplyService>();

        var licenseSection = configuration.GetSection(LicenseOptions.SectionName);
        services.Configure<LicenseOptions>(licenseSection);
        services.AddSingleton(licenseSection.Get<LicenseOptions>() ?? new LicenseOptions());
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<LicenseGenerator>();

        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IDataScopeProvider, DataScopeProvider>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
