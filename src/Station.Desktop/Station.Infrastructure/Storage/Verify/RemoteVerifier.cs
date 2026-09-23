using Microsoft.Extensions.Logging;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Verify;

/// <summary>
/// 远端校验器：按 RemoteVerifyMode 决定校验强度。
///   - None：仅大小比对；
///   - SizeAndSample：大小 + 抽样比对（首/中/尾各 4KB）；
///   - Full：下载整个文件重算 SM3。
/// 注意：抽样需要 IStorageTarget 支持随机读；本地磁盘天然支持，FTP/SFTP 使用 Range 读。
/// </summary>
public sealed class RemoteVerifier
{
    private const int SampleBlockSize = 4 * 1024;

    private readonly ILogger _logger;

    public RemoteVerifier(ILogger logger) => _logger = logger;

    public async Task VerifyAsync(
        IStorageTarget target,
        string remotePath,
        long expectedSize,
        string? expectedLocal,
        RemoteVerifyMode mode,
        CancellationToken ct)
    {
        // 1) 大小校验（永远执行，最便宜）
        var remoteSize = await target.GetRemoteSizeAsync(remotePath, ct).ConfigureAwait(false);
        if (remoteSize != expectedSize)
            throw new IOException($"[{target.Name}] 远端大小不一致：期望 {expectedSize}，实际 {remoteSize}");

        if (mode == RemoteVerifyMode.None) return;

        if (string.IsNullOrEmpty(expectedLocal))
        {
            _logger.LogDebug("[{Target}] 未提供本地摘要，跳过 SM3 校验", target.Name);
            return;
        }

        // 2) 完整（最贵）
        if (mode == RemoteVerifyMode.Full)
        {
            var remoteResult = await target.ComputeRemoteDigestAsync(remotePath, ct).ConfigureAwait(false);
            if (!string.Equals(expectedLocal, remoteResult.Digest, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"[{target.Name}] 远端 SM3 不一致：本地 {expectedLocal}，远端 {remoteResult.Digest}");
            return;
        }
        // 3) 完整
        //if (mode == RemoteVerifyMode.Full)
        //{
        //    var remoteResult = await target.SignRemoteAsync(remotePath, ct).ConfigureAwait(false);
        //    if (!string.Equals(expectedLocal, remoteResult.Digest, StringComparison.OrdinalIgnoreCase))
        //        throw new IOException($"[{target.Name}] 远端 SM3 不一致：本地 {expectedLocal}，远端 {remoteResult.Digest}");
        //}
        // 3) 抽样（SizeAndSample）：不做抽样实现的 target 直接跳过（记录日志）
        // 简化：SizeAndSample 在本期等价于 None（仅大小），预留未来扩展点。
        _logger.LogDebug("[{Target}] SizeAndSample 模式暂等价于 None，已校验大小", target.Name);
    }


}
