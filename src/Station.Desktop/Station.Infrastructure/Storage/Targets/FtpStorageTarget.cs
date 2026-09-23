using FluentFTP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Application.Storage;
using Station.Domain.Security;
using Station.Infrastructure.Storage.Pool;

namespace Station.Infrastructure.Storage;

/// <summary>
/// FTP 存储目标（连接池版 + 扩展方法加解密密码）。
/// 
/// 【密码处理流程】
///   1) 构造时：初始化 FtpConnectionHolder（Singleton 长连接，内含 keep-alive 定时器）；
///   2) 首次进入任意公开方法：await EnsurePasswordAsync → 走扩展方法解密 → 缓存明文；
///   3) 后续调用：直接命中 _plainPassword 缓存，零解密开销；
///   4) 每次 GetConnectedAsync 把已解密密码传给 holder 用于建连。
/// 
/// 【AAD 约定】
///   与 StorageConfigStore 保存密码时一致：
///   group = "storage.targets", key = "ftpPassword"
///   → AAD = "storage.targets.ftpPassword"
/// 
/// 【断点续传】
///   FluentFTP 的 FtpRemoteExists.Resume 会自动比对远端大小与本地流位置；
///   上传前额外查询远端大小做幂等判定，避免重复传输。
/// </summary>
public sealed class FtpStorageTarget : IStorageTarget, IAsyncDisposable
{
    // ---- AAD 上下文（必须与 StorageConfigStore 保存密码时保持一致） ----
    private const string PasswordGroup = "storage.targets";
    private const string PasswordKey = "ftpPassword";

    private readonly StorageTargetConfig _config;
    private readonly StorageOptions _global;
    private readonly ICryptoProviderFactory _factory;
    private readonly ICryptoPolicyService _policy;
    private readonly FtpConnectionHolder _holder;
    private readonly ILogger _logger;

    // ---- 明文密码缓存（首次解密后填充） ----
    private string? _plainPassword;
    private readonly SemaphoreSlim _passwordLock = new(1, 1);
    private bool _disposed;

    public FtpStorageTarget(
        StorageTargetConfig config,
        IOptions<StorageOptions> options,
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        ILogger logger)
    {
        _config = config;
        _global = options.Value;
        _factory = factory;
        _policy = policy;
        _logger = logger;

        // 连接持有者由 target 内部构造（含 keep-alive 定时器）。
        // 生命周期与 target 一致（target 由 DI 作为 Singleton 注册）。
        var keepAlive = config.FtpKeepAliveSeconds ?? options.Value.FtpKeepAliveSeconds;
        _holder = new FtpConnectionHolder(config, keepAlive, logger);
    }

    public string Name => _config.Name ?? "ftp";
    public StorageTargetKind Kind => StorageTargetKind.Ftp;

    // =========================================================
    // IStorageTarget 实现
    // =========================================================

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            await using var stream = file.OpenRead();

            // ---- 幂等 + 续传判定 ----
            long remoteSize = 0;
            try
            {
                remoteSize = await ftp.GetFileSize(file.RemotePath, 0, ct).ConfigureAwait(false);
            }
            catch
            {
                // 文件不存在或其他远端查询失败 → 视为从头开始。
                // 注意：这里不 MarkBroken，因为"文件不存在"是正常情况。
                remoteSize = 0;
            }

            if (remoteSize == file.Size)
            {
                _logger.LogDebug("[{Target}] 目标已完整，跳过：{RemotePath}",
                    Name, file.RemotePath);
                if (onProgress is not null) await onProgress(1).ConfigureAwait(false);
                return;
            }

            if (remoteSize > file.Size)
            {
                // 远端比本地大（历史残留/篡改）→ 删除重传
                _logger.LogWarning(
                    "[{Target}] 远端文件比本地大（{Remote}>{Local}），删除重传：{RemotePath}",
                    Name, remoteSize, file.Size, file.RemotePath);
                await ftp.DeleteFile(file.RemotePath, ct).ConfigureAwait(false);
                remoteSize = 0;
            }

            // ---- 本地流定位到断点处 ----
            if (remoteSize > 0)
            {
                stream.Seek(remoteSize, SeekOrigin.Begin);
            }

            // ---- 进度回调（FluentFTP 的 Progress 是 0~100） ----
            var progress = onProgress is null
                ? null
                : new Progress<FtpProgress>(p =>
                {
                    // FluentFTP 进度是 0~100，我们转成 0~1
                    _ = onProgress(Math.Clamp(p.Progress / 100d, 0, 1));
                });

            // ---- 上传（Resume 模式） ----
            var status = await ftp.UploadStream(
                stream,
                file.RemotePath,
                FtpRemoteExists.Resume,
                createRemoteDir: true,
                progress: progress,
                token: ct).ConfigureAwait(false);

            // Skipped 表示远端已完整，等于成功；Failed 才是错误
            if (status == FtpStatus.Failed)
            {
                throw new IOException($"[{Name}] FTP 上传失败：{status}");
            }
        }
        catch
        {
            // 任何异常都标记连接失效，下次调用会重连
            _holder.MarkBroken();
            throw;
        }
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            return await ftp.GetFileSize(remotePath, 0, ct).ConfigureAwait(false);
        }
        catch
        {
            _holder.MarkBroken();
            throw;
        }
    }

    public async Task<RemoteDigestResult> ComputeRemoteDigestAsync(
        string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        // 读 file_sig 策略决定摘要算法（默认 SM3）
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var hasher = _factory.GetHasher(policy.Algorithm);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            await using var stream = await ftp
                .OpenRead(remotePath, FtpDataType.Binary, 0, true, ct)
                .ConfigureAwait(false);

            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return new RemoteDigestResult(digest, policy.Algorithm);
        }
        catch
        {
            _holder.MarkBroken();
            throw;
        }
    }

    public async Task<RemoteSignatureResult> SignRemoteAsync(
        string remotePath, string privateKeyPem, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var hasher = _factory.GetHasher(policy.Algorithm);
        var signAlgo = policy.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;
        var signer = _factory.GetSigner(signAlgo);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            await using var stream = await ftp
                .OpenRead(remotePath, FtpDataType.Binary, 0, true, ct)
                .ConfigureAwait(false);

            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);

            // 对摘要（Base64 解码后的字节）签名
            var signature = signer.Sign(Convert.FromBase64String(digest), privateKeyPem);
            return new RemoteSignatureResult(digest, policy.Algorithm, signature, signAlgo);
        }
        catch
        {
            _holder.MarkBroken();
            throw;
        }
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            if (await ftp.FileExists(remotePath, ct).ConfigureAwait(false))
            {
                await ftp.DeleteFile(remotePath, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            _holder.MarkBroken();
            throw;
        }
    }

    // =========================================================
    // 密码解析（核心变更点）
    // =========================================================

    /// <summary>
    /// 确保明文密码已解析。首次调用走扩展方法解密并缓存，后续直接命中。
    /// 
    /// 走 CryptoSecretExtensions.TryUnprotectSecretAsync：
    ///   - 历史明文（非 v{n}: 前缀）→ 原样返回 + WARN 日志；
    ///   - 密文解密失败 → 返回 null + ERROR 日志 → 退化为空串（后续 FTP 认证失败，日志可见）；
    ///   - 策略/工厂异常 → 向上抛（系统级故障，不吞）。
    /// 
    /// 使用 SemaphoreSlim 保护首次解密：并发首调时只有一个线程真正解密。
    /// </summary>
    private async Task EnsurePasswordAsync(CancellationToken ct)
    {
        if (_plainPassword is not null) return;

        await _passwordLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // double-check：等待锁期间可能其他线程已完成
            if (_plainPassword is not null) return;

            var plain = await _factory.TryUnprotectAsync(
                _policy,
                PasswordGroup,
                PasswordKey,
                _config.FtpPassword,
                _logger,
                ct).ConfigureAwait(false);

            // 解密失败（null）→ 空串（后续 FTP 认证自然报错，日志能看出问题）
            _plainPassword = plain ?? string.Empty;

            if (_plainPassword.Length == 0 && !string.IsNullOrEmpty(_config.FtpPassword))
            {
                _logger.LogWarning(
                    "[{Target}] FTP 密码解密结果为空，可能是主密钥丢失或密文损坏",
                    Name);
            }
        }
        finally
        {
            _passwordLock.Release();
        }
    }

    // =========================================================
    // 释放
    // =========================================================

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _passwordLock.Dispose();
        await _holder.DisposeAsync().ConfigureAwait(false);
    }
}
