using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security.Abstractions;
using Station.Application.Storage;
using Station.Infrastructure.Storage.CircuitBreaker;
using Station.Infrastructure.Storage.Retry;
using Station.Infrastructure.Storage.Targets;
using Station.Infrastructure.Storage.Telemetry;
using Station.Infrastructure.Storage.Verify;

namespace Station.Infrastructure.Storage;

/// <summary>存储服务注册。装饰器链：Retry → CircuitBreaker → Telemetry → 具体实现。</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStationStorage(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        //// 配置只读视图
        //services.AddSingleton<IStorageConfiguration>(sp =>
        //    sp.GetRequiredService<IOptions<StorageOptions>>().Value);

        // 遥测
        services.AddMetrics();
        services.AddSingleton<StorageMetrics>();

        // 校验器
        services.AddSingleton<RemoteVerifier>(sp =>
            new RemoteVerifier(sp.GetRequiredService<ILogger<RemoteVerifier>>()));

        // 并发限流（Singleton，进程级）
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var size = opts.MaxConcurrentUploads > 0 ? opts.MaxConcurrentUploads : int.MaxValue;
            return new SemaphoreSlim(size, size);
        });

        // 多 target（Singleton）
        services.AddSingleton<IReadOnlyList<IStorageTarget>>(BuildTargets);

        // 服务
        services.AddScoped<IStorageService, StorageService>();

        return services;
    }

    private static IReadOnlyList<IStorageTarget> BuildTargets(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
        var metrics = sp.GetRequiredService<StorageMetrics>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        var list = new List<IStorageTarget>();

        for (var i = 0; i < opts.Targets.Count; i++)
        {
            var cfg = opts.Targets[i];
            if (!cfg.Enabled) continue;

            // 唯一名称
            cfg.Name ??= $"{cfg.Kind.ToString().ToLowerInvariant()}-{i}";

            var logger = loggerFactory.CreateLogger($"Storage.{cfg.Name}");

            // 内层具体实现
            IStorageTarget inner = cfg.Kind switch
            {
                StorageTargetKind.Local => new LocalDiskStorageTarget(
                    cfg, sp.GetRequiredService<IOptions<StorageOptions>>(), sp.GetRequiredService<ICryptoPolicyService>(), sp.GetRequiredService<ICryptoProviderFactory>(), logger),

                StorageTargetKind.Ftp => BuildFtp(cfg, opts, sp, logger),

                StorageTargetKind.Sftp => BuildSftp(cfg, opts, sp, logger),

                _ => throw new NotSupportedException($"不支持的目标类型：{cfg.Kind}")
            };

            // 装饰器链（顺序：Telemetry → CircuitBreaker → Retry）
            inner = new TelemetryStorageTarget(inner, metrics, logger);

            var breaker = new StorageCircuitBreaker(
                cfg.Name,
                cfg.CircuitBreakerThreshold ?? opts.CircuitBreakerThreshold,
                cfg.CircuitBreakerCooldownSeconds ?? opts.CircuitBreakerCooldownSeconds);

            // 熔断状态变更 → 记录 Metrics + 审计
            breaker.StateChanged += (_, e) =>
            {
                if (e.NewState == StorageCircuitState.Open)
                    metrics.RecordCircuitOpen(e.TargetName);
                logger.LogWarning("[{Target}] 熔断状态 {Old} → {New}（连续失败 {Failures}）",
                    e.TargetName, e.OldState, e.NewState, e.ConsecutiveFailures);
            };

            inner = new CircuitBreakerStorageTarget(inner, breaker);

            var retryOpts = new RetryOptions(
                MaxAttempts: cfg.RetryCount ?? opts.RetryCount,
                BaseDelaySeconds: cfg.RetryIntervalSeconds ?? opts.RetryIntervalSeconds,
                MaxDelaySeconds: opts.MaxRetryDelaySeconds,
                JitterFactor: opts.RetryJitterFactor);

            inner = new RetryStorageTarget(inner, retryOpts, logger);

            list.Add(inner);
        }

        return list;
    }

    private static IStorageTarget BuildFtp(
        StorageTargetConfig cfg,
        StorageOptions opts,
        IServiceProvider sp,
        ILogger logger)
    {
        return new FtpStorageTarget(
            cfg,
            sp.GetRequiredService<IOptions<StorageOptions>>(),
            sp.GetRequiredService<ICryptoPolicyService>(),
            sp.GetRequiredService<ICryptoProviderFactory>(),
            logger);
    }

    private static IStorageTarget BuildSftp(
        StorageTargetConfig cfg,
        StorageOptions opts,
        IServiceProvider sp,
        ILogger logger)
    {
        return new SftpStorageTarget(
             cfg,
             sp.GetRequiredService<IOptions<StorageOptions>>(),
             sp.GetRequiredService<ICryptoPolicyService>(),
             sp.GetRequiredService<ICryptoProviderFactory>(),
             logger);
    }
}
