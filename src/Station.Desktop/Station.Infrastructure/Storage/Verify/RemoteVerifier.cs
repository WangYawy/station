using Microsoft.Extensions.Logging;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Verify;

/// <summary>
/// 远端校验器：按 RemoteVerifyMode / RemoteContentMode 决定校验策略。
/// 
/// 【Plaintext 模式】
///   - None / SizeAndSample / Full：按 RemoteVerifyMode 走；
///   - 可重算摘要（远端存明文）。
/// 
/// 【Ciphertext 模式】
///   - 仅 size 校验（远端存密文，与明文摘要不等）；
///   - 明文摘要校验由平台侧（持有密钥）负责。
/// </summary>
public sealed class RemoteVerifier
{
    private readonly ILogger<RemoteVerifier> _logger;

    public RemoteVerifier(ILogger<RemoteVerifier> logger) => _logger = logger;

    public async Task VerifyAsync(
        IStorageTarget target,
        string remotePath,
        long expectedSize,
        string? expectedContentDigest,
        RemoteVerifyMode verifyMode,
        RemoteContentMode contentMode,
        CancellationToken ct)
    {
        // =========================================================
        // 1) 大小校验（两种模式都执行）
        // =========================================================
        var remoteSize = await target.GetRemoteSizeAsync(remotePath, ct).ConfigureAwait(false);

        if (contentMode == RemoteContentMode.Ciphertext)
        {
            // 密文模式下 fileSize 传的是密文大小
            if (remoteSize != expectedSize)
                throw new IOException(
                    $"[{target.Name}] 远端大小不一致：期望 {expectedSize}，实际 {remoteSize}");
            return;
        }

        // =========================================================
        // 2) 明文模式：按 RemoteVerifyMode 决定摘要校验
        // =========================================================
        if (remoteSize != expectedSize)
            throw new IOException(
                $"[{target.Name}] 远端大小不一致：期望 {expectedSize}，实际 {remoteSize}");

        if (verifyMode == RemoteVerifyMode.None) return;

        if (string.IsNullOrEmpty(expectedContentDigest))
        {
            _logger.LogDebug("[{Target}] 未提供摘要，跳过 SM3 校验", target.Name);
            return;
        }

        if (verifyMode == RemoteVerifyMode.Full)
        {
            var remoteDigest = await target.ComputeRemoteDigestAsync(remotePath, ct)
                .ConfigureAwait(false);

            if (!string.Equals(expectedContentDigest, remoteDigest.Digest, StringComparison.OrdinalIgnoreCase))
                throw new IOException(
                    $"[{target.Name}] 远端摘要不一致：本地 {expectedContentDigest}，远端 {remoteDigest}");

            return;
        }

        // SizeAndSample 模式暂等价于 None（未来扩展抽样读）
        _logger.LogDebug("[{Target}] SizeAndSample 模式暂等价于 None，已校验大小", target.Name);
    }
}
