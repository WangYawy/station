using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Crypto;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// 远程主密钥提供者（集群版，骨架）。
///
/// 【未来实现路径】
///   1. 采集站首次启动：生成节点身份密钥对（从硬件指纹派生或本地生成）
///   2. 向平台注册节点，平台返回包裹后的客户 KEK（用节点公钥加密）
///   3. 用节点私钥解出 KEK，缓存在本地（再用节点身份密钥加密落盘）
///   4. KEK 轮换：平台重新下发，本地用新 KEK 重加密 DEK
///
/// 【当前状态】仅接口就位，调用抛 NotSupportedException。
/// </summary>
internal sealed class RemoteMasterKeyProvider : IMasterKeyProvider
{
    private readonly CryptoOptions _options;
    private readonly ILogger _logger;

    public RemoteMasterKeyProvider(
        IOptions<CryptoOptions> options,
        ILogger logger)
    {
        _options = options.Value;
        _logger = logger;
        _logger.LogWarning("RemoteMasterKeyProvider 尚未实现，当前为骨架");
    }

    public int CurrentVersion =>
        throw new NotSupportedException("集群模式尚未启用，请将 CryptoOptions.Mode 设为 Local");

    public IReadOnlyList<int> GetVersions() =>
        throw new NotSupportedException("集群模式尚未启用");

    public byte[] GetKey(int version) =>
        throw new NotSupportedException("集群模式尚未启用");

    public Task<int> RotateAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("集群模式尚未启用");

    public byte[] DeriveBindingKey() =>
        throw new NotSupportedException("集群模式尚未启用");

    public byte[] DeriveFileEncryptionKey(int keyVersion) =>
        throw new NotSupportedException("集群模式尚未启用");
}
