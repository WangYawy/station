using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application.Alerts;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Authorization;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Recorders;
using Station.Application.Storage;
using Station.Application.Uploading;
using Station.Application.UsbPortCard;
using Station.Application.UsbPortCard.Events;
using Station.Application.Users;

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
        // 注册认证授权配置
        var authSection = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(authSection);
        services.AddSingleton(authSection.Get<AuthOptions>() ?? new AuthOptions());
        // 注册采集配置
        var collectSection = configuration.GetSection(CollectOptions.SectionName);
        var collectOptions = collectSection.Get<CollectOptions>() ?? new CollectOptions();
        // .NET 配置绑定对 List 是"追加"而非替换：存在配置项时按配置整体重建白名单，避免与默认值叠加
        if (collectSection.GetSection(nameof(CollectOptions.FileExtensions)).Exists())
        {
            collectOptions.FileExtensions =
                collectSection.GetSection(nameof(CollectOptions.FileExtensions)).Get<List<string>>() ?? [];
        }

        services.AddSingleton(collectOptions);
        services.AddSingleton(Options.Create(collectOptions));
        // 注册采集任务服务和文件缓存清理服务
        services.AddScoped<ICacheCleanupService, CacheCleanupService>();
        services.AddScoped<ICollectTaskService, CollectTaskService>();
        // 注册记录仪相关服务
        var bindingSection = configuration.GetSection(BindingOptions.SectionName);
        services.Configure<BindingOptions>(bindingSection);
        services.AddSingleton<RecorderBindingFile>();
        services.AddScoped<IRecorderService, RecorderService>(); // 记录仪绑定服务
        services.AddScoped<IRecorderIdentificationService, RecorderIdentificationService>(); // 记录仪绑定识别服务
        // 注册报警服务
        services.AddScoped<IAlertService, AlertService>();
        //var storageSection = configuration.GetSection(StorageOptions.SectionName);
        //services.Configure<StorageOptions>(storageSection);
        //services.AddSingleton(storageSection.Get<StorageOptions>() ?? new StorageOptions());
        //services.AddSingleton<IStorageCircuitBreaker>(sp =>
        //{
        //    var storage = sp.GetRequiredService<StorageOptions>();
        //    return new StorageCircuitBreaker(storage.CircuitBreakerThreshold, storage.CircuitBreakerCooldownSeconds);
        //});
        //services.AddSingleton<IStorageTarget>(sp =>
        //{
        //    var storage = sp.GetRequiredService<StorageOptions>();
        //    return storage.Target switch
        //    {
        //        StorageTargetKind.Ftp => new FtpStorageTarget(Options.Create(storage)),
        //        StorageTargetKind.Sftp => new SftpStorageTarget(Options.Create(storage)),
        //        _ => new LocalDiskStorageTarget(Options.Create(storage))
        //    };
        //});
        // 注册上传服务
        services.AddScoped<IUploadService, UploadService>();
        services.AddSingleton<IDirectoryTemplateRenderer, DirectoryTemplateRenderer>();

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
        services.AddScoped<IUsbPortCardEventService, UsbPortCardEventService>();
        services.AddScoped<IUsbPortCardService, UsbPortCardService>();
        // 注册授权服务
        var licenseSection = configuration.GetSection(LicenseOptions.SectionName);
        services.Configure<LicenseOptions>(licenseSection);
        services.AddSingleton(licenseSection.Get<LicenseOptions>() ?? new LicenseOptions());
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<LicenseGenerator>();
        // 注册审计日志服务
        services.AddScoped<IAuditLogService, AuditLogService>();
        // 注册授权服务
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        // 注册数据权限服务
        services.AddScoped<IDataScopeProvider, DataScopeProvider>();
        // 注册登录认证服务
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        // 注册RBAC服务
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
