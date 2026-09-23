using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Application.Storage;
using Station.Infrastructure.Storage.Pool;

namespace Station.Infrastructure.Storage;

/// <summary>
/// SFTP 存储目标（连接池版 + 扩展方法加解密密码）。
/// 
/// 【密码处理流程】
///   1) 构造时：初始化 SftpConnectionPool，其密码 provider 闭包访问 _plainPassword；
///   2) 首次进入任意公开方法：await EnsurePasswordAsync → 走扩展方法解密 → 缓存明文；
///   3) 后续调用：直接命中 _plainPassword 缓存，零解密开销；
///   4) 连接池内部创建 SftpClient 时通过 provider 拿到已解密密码。
/// 
/// 【AAD 约定】
///   与 StorageConfigStore 保存密码时一致：group="storage.targets", key="sftpPassword"
///   → AAD = "storage.targets.sftpPassword"
/// 
/// 【断点续传】
///   先查远端大小，本地 Seek + 远端 FileMode.Append。
/// </summary>
public sealed class SftpStorageTarget : IStorageTarget, IAsyncDisposable
{
    // ---- AAD 上下文（必须与 StorageConfigStore 保存密码时保持一致） ----
    private const string PasswordGroup = "storage.targets";
    private const string PasswordKey = "sftpPassword";

    private readonly StorageTargetConfig _config;
    private readonly StorageOptions _global;
    private readonly ICryptoProviderFactory _factory;
    private readonly ICryptoPolicyService _policy;
    private readonly SftpConnectionPool _pool;
    private readonly ILogger _logger;

    // ---- 明文密码缓存（首次解密后填充） ----
    private string? _plainPassword;
    private readonly SemaphoreSlim _passwordLock = new(1, 1);
    private bool _disposed;

    public SftpStorageTarget(
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

        var poolSize = config.SftpPoolSize ?? options.Value.SftpPoolSize;
        var keepAlive = config.SftpKeepAliveSeconds ?? options.Value.SftpKeepAliveSeconds;

        // 关键：连接池由 target 内部构造，provider 闭包访问 _plainPassword 字段。
        // 每次 pool 创建 SftpClient 时，会调用 () => _plainPassword ?? string.Empty。
        // 由于所有公开方法入口先 EnsurePasswordAsync，pool 拿到的密码一定是已解密的。
        _pool = new SftpConnectionPool(
            config,
            poolSize,
            keepAlive,
            passwordProvider: () => _plainPassword ?? string.Empty,
            logger: logger);
    }

    public string Name => _config.Name ?? "sftp";
    public StorageTargetKind Kind => StorageTargetKind.Sftp;

    // =========================================================
    // IStorageTarget 实现
    // =========================================================

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var sftp = _pool.Rent();
        try
        {
            var root = ResolveRoot(sftp);
            var remotePath = AbsolutePath(root, file.RemotePath);
            EnsureRemoteDirectories(sftp, RemoteDirectory(remotePath));

            // ---- 幂等 + 续传判定 ----
            long remoteSize = 0;
            try { remoteSize = sftp.GetAttributes(remotePath).Size; }
            catch (SftpPathNotFoundException) { /* 远端不存在，从头开始 */ }

            if (remoteSize == file.Size)
            {
                _logger.LogDebug("[{Target}] 目标已完整，跳过：{RemotePath}", Name, file.RemotePath);
                if (onProgress is not null) await onProgress(1).ConfigureAwait(false);
                return;
            }

            if (remoteSize > file.Size)
            {
                // 远端比本地大（历史残留/篡改），删除重传
                _logger.LogWarning("[{Target}] 远端文件比本地大（{Remote}>{Local}），删除重传：{RemotePath}",
                    Name, remoteSize, file.Size, file.RemotePath);
                sftp.DeleteFile(remotePath);
                remoteSize = 0;
            }

            // ---- 本地流定位到断点处 ----
            await using var input = file.OpenRead();
            if (remoteSize > 0) input.Seek(remoteSize, SeekOrigin.Begin);

            // ---- 远端追加写入 ----
            using var remoteStream = sftp.Open(remotePath, FileMode.Append, FileAccess.Write);

            var buffer = new byte[_global.ChunkBytes];
            long total = remoteSize;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                remoteStream.Write(buffer, 0, read);
                total += read;
                if (file.Size > 0 && onProgress is not null)
                    await onProgress((double)total / file.Size).ConfigureAwait(false);
            }

            remoteStream.Flush();
            if (file.Size > 0 && onProgress is not null) await onProgress(1).ConfigureAwait(false);
        }
        finally
        {
            _pool.Return(sftp);
        }
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var sftp = _pool.Rent();
        try
        {
            var path = AbsolutePath(ResolveRoot(sftp), remotePath);
            return sftp.GetAttributes(path).Size;
        }
        finally { _pool.Return(sftp); }
    }

    public async Task<RemoteDigestResult> ComputeRemoteDigestAsync(string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        // 读 file_sig 策略决定摘要算法（默认 SM3）
        var policy = await _policy.GetAsync(Station.Domain.Security.CryptoUsage.FileSig, ct)
            .ConfigureAwait(false);
        var hasher = _factory.GetHasher(policy.Algorithm);

        var sftp = _pool.Rent();
        try
        {
            var path = AbsolutePath(ResolveRoot(sftp), remotePath);
            using var stream = sftp.OpenRead(path);
            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return new RemoteDigestResult(digest, policy.Algorithm);
        }
        finally { _pool.Return(sftp); }
    }

    public async Task<RemoteSignatureResult> SignRemoteAsync(
        string remotePath, string privateKeyPem, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var policy = await _policy.GetAsync(Station.Domain.Security.CryptoUsage.FileSig, ct)
            .ConfigureAwait(false);
        var hasher = _factory.GetHasher(policy.Algorithm);
        var signAlgo = policy.SecondaryAlgorithm ?? Station.Domain.Security.CryptoAlgorithm.Sm2Sm3;
        var signer = _factory.GetSigner(signAlgo);

        var sftp = _pool.Rent();
        try
        {
            var path = AbsolutePath(ResolveRoot(sftp), remotePath);
            using var stream = sftp.OpenRead(path);
            var digest = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);

            var signature = signer.Sign(Convert.FromBase64String(digest), privateKeyPem);
            return new RemoteSignatureResult(digest, policy.Algorithm, signature, signAlgo);
        }
        finally { _pool.Return(sftp); }
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        await EnsurePasswordAsync(ct).ConfigureAwait(false);

        var sftp = _pool.Rent();
        try
        {
            var path = AbsolutePath(ResolveRoot(sftp), remotePath);
            if (sftp.Exists(path)) sftp.DeleteFile(path);
        }
        finally { _pool.Return(sftp); }
    }

    // =========================================================
    // 密码解析（核心变更点）
    // =========================================================

    /// <summary>
    /// 确保明文密码已解析。首次调用走扩展方法解密并缓存，后续直接命中。
    /// 
    /// 走 CryptoSecretExtensions.TryUnprotectSecretAsync：
    ///   - 历史明文（非 v{n}: 前缀）→ 原样返回 + WARN 日志；
    ///   - 密文解密失败 → 返回 null + ERROR 日志 → 退化为空串（后续 SSH 认证失败，日志可见）；
    ///   - 策略/工厂异常 → 向上抛（系统级故障）。
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

            var plain = await _factory.TryUnprotectAsync(
                _policy,
                PasswordGroup,
                PasswordKey,
                _config.SftpPassword,
                _logger,
                ct).ConfigureAwait(false);

            // 解密失败（null）→ 空串（后续 SSH 认证自然报错，日志能看出问题）
            _plainPassword = plain ?? string.Empty;

            if (_plainPassword.Length == 0 && !string.IsNullOrEmpty(_config.SftpPassword))
            {
                _logger.LogWarning(
                    "[{Target}] SFTP 密码解密结果为空，可能是主密钥丢失或密文损坏",
                    Name);
            }
        }
        finally
        {
            _passwordLock.Release();
        }
    }

    // =========================================================
    // 路径处理
    // =========================================================

    private string ResolveRoot(SftpClient sftp) =>
        string.IsNullOrWhiteSpace(_config.SftpRoot)
            ? sftp.WorkingDirectory.TrimEnd('/')
            : _config.SftpRoot.TrimEnd('/');

    private static string AbsolutePath(string root, string remotePath) =>
        $"{root}/{remotePath.TrimStart('/')}";

    private static string RemoteDirectory(string remotePath)
    {
        var idx = remotePath.LastIndexOf('/');
        return idx > 0 ? remotePath[..idx] : "/";
    }

    /// <summary>
    /// 逐级创建远端目录。
    /// 并发场景下另一线程可能已创建 → 捕获 SshException 视为成功。
    /// </summary>
    private static void EnsureRemoteDirectories(SftpClient sftp, string remoteDir)
    {
        var current = string.Empty;
        foreach (var seg in remoteDir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + seg;
            try
            {
                if (!sftp.Exists(current)) sftp.CreateDirectory(current);
            }
            catch (SshException)
            {
                // 并发创建竞态：目录已被其他线程建好，忽略
            }
        }
    }

    /// <summary>
    /// 释放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _passwordLock.Dispose();
        _pool.Dispose();
        await ValueTask.CompletedTask;
    }
}
