using System.Net.Sockets;
using FluentFTP.Exceptions;
using Polly;
using Polly.Retry;
using Renci.SshNet.Common;
using Station.Infrastructure.Storage.CircuitBreaker;

namespace Station.Infrastructure.Storage.Retry;

/// <summary>
/// Polly 重试策略工厂。
/// 
/// 设计要点：
///   1) 指数退避 + 抖动：避免重试风暴打爆远端；
///   2) 熔断打开异常不重试（立即终止）；
///   3) 认证/参数/文件不存在类异常不重试（重试无意义）；
///   4) 网络类异常可重试。
/// </summary>
public static class StorageRetryPolicies
{
    public static ResiliencePipeline Build(RetryOptions options)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, options.MaxAttempts - 1),   // Polly 语义是"重试次数"，不含首次
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(options.BaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(options.MaxDelaySeconds),
                UseJitter = options.JitterFactor > 0,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ShouldRetry(ex)),
                OnRetry = args =>
                {
                    options.OnRetry?.Invoke(args.AttemptNumber, args.RetryDelay, args.Outcome.Exception);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>判断异常是否应当重试。</summary>
    public static bool ShouldRetry(Exception ex) => ex switch
    {
        // 明确不重试
        OperationCanceledException => false,
        CircuitBreakerOpenException => false,
        UnauthorizedAccessException => false,
        FileNotFoundException => false,
        DirectoryNotFoundException => false,
        ArgumentException => false,

        // FTP 认证/命令错误不重试（4xx 是客户端错误，5xx 才重试）
        FtpAuthenticationException => false,
        FtpCommandException ftpCmd => ftpCmd.CompletionCode is not null
                                      && (ftpCmd.CompletionCode.StartsWith("4") || ftpCmd.CompletionCode.StartsWith("5")),

        // SFTP 认证失败不重试
        SshAuthenticationException => false,

        // 网络类可重试
        IOException => true,
        SocketException => true,
        TimeoutException => true,
        SshException => true,                        // 连接中断、协议错误
        HttpRequestException => true,                // 若未来接入 HTTP 存储

        _ => false
    };
}

/// <summary>重试选项（由 StorageOptions 派生）。</summary>
public sealed record RetryOptions(
    int MaxAttempts,
    int BaseDelaySeconds,
    int MaxDelaySeconds,
    double JitterFactor,
    Action<int, TimeSpan, Exception?>? OnRetry = null);
