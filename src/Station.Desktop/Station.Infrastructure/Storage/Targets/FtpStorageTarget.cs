using FluentFTP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
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
/// 【依赖原则】
///   本类只依赖 Application 层的 ICryptoPolicyService 接口；
///   具体算法实现由 Station.Crypto 提供，通过策略服务路由。
/// </summary>
public sealed class FtpStorageTarget : IStorageTarget, IAsyncDisposable
{
    // ---- AAD 上下文（必须与 StorageConfigStore 保存密码时保持一致） ----
    private const string PasswordGroup = "storage.targets";
    private const string PasswordKey = "ftpPassword";

    private readonly StorageTargetConfig _config;
    private readonly StorageOptions _global;
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
        ILogger logger)
    {
        _config = config;
        _global = options.Value;
        _policy = policy;
        _logger = logger;

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
                // 文件不存在或其他远端查询失败 → 视为从头开始
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

            if (status == FtpStatus.Failed)
            {
                throw new IOException($"[{Name}] FTP 上传失败：{status}");
            }
        }
        catch
        {
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

        // 走策略服务取摘要器（IHasher 自带 Algorithm 属性）
        var hasher = await _policy.GetHasherAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            await using var stream = await ftp
                .OpenRead(remotePath, FtpDataType.Binary, 0, true, ct)
                .ConfigureAwait(false);

            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return new RemoteDigestResult(digest, hasher.Algorithm);
        }
        catch
        {
            _holder.MarkBroken();
            throw;
        }
    }

    /// <summary>
    /// 对远端文件摘要签名。
    /// 
    /// ⚠️ 注意：SM2 签名内部已含 SM3 摘要，本方法对"摘要的字节"再签名属于双重哈希。
    ///     当前保留原逻辑以兼容既有数据；如未来启用国密合规审计，
    ///     应改为对"原始文件流"签名（见 SM2Signer 内部实现）。
    /// </summary>
    public async Task<RemoteSignatureResult> SignRemoteAsync(
        string remotePath, string privateKeyPem, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var hasher = await _policy.GetHasherAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var signer = await _policy.GetSignerAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);

        var ftp = await _holder.GetConnectedAsync(_plainPassword ?? string.Empty, ct)
            .ConfigureAwait(false);

        try
        {
            await using var stream = await ftp
                .OpenRead(remotePath, FtpDataType.Binary, 0, true, ct)
                .ConfigureAwait(false);

            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            var signature = signer.Sign(Convert.FromBase64String(digest), privateKeyPem);

            return new RemoteSignatureResult(digest, hasher.Algorithm, signature, signer.Algorithm);
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
    // 密码解析
    // =========================================================

    /// <summary>
    /// 确保明文密码已解析。首次调用走扩展方法解密并缓存，后续直接命中。
    /// 
    /// 走 CryptoExtensions.TryUnprotectSecretAsync：
    ///   - 历史明文（非 v{n}: 前缀）→ 原样返回 + WARN 日志；
    ///   - 密文解密失败 → 返回 null + ERROR 日志 → 退化为空串（后续 FTP 认证失败，日志可见）；
    ///   - 策略异常 → 向上抛（系统级故障）。
    /// 
    /// 使用 SemaphoreSlim 保护首次解密：并发首调时只有一个线程真正解密。
    /// </summary>
    private async Task EnsurePasswordAsync(CancellationToken ct)
    {
        if (_plainPassword is not null) return;

        await _passwordLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_plainPassword is not null) return;

            var plain = await _policy.TryUnprotectSecretAsync(
                PasswordGroup,
                PasswordKey,
                _config.FtpPassword,
                _logger,
                ct).ConfigureAwait(false);

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
