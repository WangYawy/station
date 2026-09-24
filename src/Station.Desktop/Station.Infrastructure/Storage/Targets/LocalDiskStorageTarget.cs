using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Application.Storage;
using Station.Domain.Security;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 本地磁盘存储目标。
/// 
/// 【特殊说明】
///   "远端路径"就是本地文件路径，所以摘要/签名直接读本地文件。
///   仍然走 ICryptoPolicyService 保持与其他 Target 一致的依赖结构。
/// </summary>
public sealed class LocalDiskStorageTarget : IStorageTarget
{
    private readonly StorageTargetConfig _config;
    private readonly StorageOptions _global;
    private readonly ICryptoPolicyService _policy;
    private readonly ILogger _logger;

    public LocalDiskStorageTarget(
        StorageTargetConfig config,
        IOptions<StorageOptions> options,
        ICryptoPolicyService policy,
        ILogger logger)
    {
        _config = config;
        _global = options.Value;
        _policy = policy;
        _logger = logger;
    }

    public string Name => _config.Name ?? "local";
    public StorageTargetKind Kind => StorageTargetKind.Local;

    // =========================================================
    // 上传（本地复制）
    // =========================================================

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken ct)
    {
        var root = _config.LocalRoot
            ?? throw new InvalidOperationException($"[{Name}] 未配置 LocalRoot");
        var destination = ResolveLocalPath(root, file.RemotePath);

        // 幂等：目标已完整则跳过
        if (File.Exists(destination) && new FileInfo(destination).Length == file.Size)
        {
            _logger.LogDebug("[{Target}] 目标已存在且大小一致，跳过：{Path}", Name, destination);
            if (onProgress is not null) await onProgress(1).ConfigureAwait(false);
            return;
        }

        var dir = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(dir);

        // 断点续传：已有部分文件则追加
        long existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;
        if (existing > file.Size)
        {
            File.Delete(destination);
            existing = 0;
        }

        await using var input = file.OpenRead();
        if (existing > 0) input.Seek(existing, SeekOrigin.Begin);

        await using var output = new FileStream(
            destination,
            existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, _global.ChunkBytes, useAsync: true);

        var buffer = new byte[_global.ChunkBytes];
        long total = existing;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
            if (file.Size > 0 && onProgress is not null)
                await onProgress((double)total / file.Size).ConfigureAwait(false);
        }
        await output.FlushAsync(ct).ConfigureAwait(false);
        if (file.Size > 0 && onProgress is not null) await onProgress(1).ConfigureAwait(false);
    }

    // =========================================================
    // 远端大小
    // =========================================================

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct)
    {
        var path = ResolveLocalPath(_config.LocalRoot!, remotePath);
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        return Task.FromResult(new FileInfo(path).Length);
    }

    // =========================================================
    // 远端摘要
    // =========================================================

    public async Task<RemoteDigestResult> ComputeRemoteDigestAsync(
        string remotePath, CancellationToken ct)
    {
        var hasher = await _policy.GetHasherAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var path = ResolveLocalPath(_config.LocalRoot!, remotePath);

        await using var fs = File.OpenRead(path);
        var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return new RemoteDigestResult(digest, hasher.Algorithm);
    }

    // =========================================================
    // 远端签名
    // =========================================================

    /// <summary>
    /// 对本地文件摘要签名（本地场景"远端"即本地）。
    /// 
    /// ⚠️ 与 FtpStorageTarget 同样的双重哈希注意点（见该类注释）。
    /// </summary>
    public async Task<RemoteSignatureResult> SignRemoteAsync(
        string remotePath, string privateKeyPem, CancellationToken ct)
    {
        var hasher = await _policy.GetHasherAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var signer = await _policy.GetSignerAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);

        var path = ResolveLocalPath(_config.LocalRoot!, remotePath);
        await using var fs = File.OpenRead(path);
        var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);

        var signature = signer.Sign(Convert.FromBase64String(digest), privateKeyPem);
        return new RemoteSignatureResult(digest, hasher.Algorithm, signature, signer.Algorithm);
    }

    // =========================================================
    // 删除
    // =========================================================

    public Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        var path = ResolveLocalPath(_config.LocalRoot!, remotePath);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // =========================================================
    // 路径规范化（防目录穿越）
    // =========================================================

    /// <summary>规范化路径（防目录穿越，统一分隔符）。</summary>
    internal static string ResolveLocalPath(string root, string remotePath)
    {
        var normalized = remotePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(".."))
            throw new InvalidOperationException($"非法路径：{remotePath}");
        return Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar));
    }
}
