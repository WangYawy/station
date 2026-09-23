using System.Collections.Concurrent;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>
/// 算法工厂：算法名 → 实现。所有 provider 均为单例，线程安全。
/// 内部字典缓存（O(1)），运行时零反射。
/// </summary>
public sealed class CryptoProviderFactory : ICryptoProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPasswordHasher> _hashers;
    private readonly IReadOnlyDictionary<string, ISigner> _signers;
    private readonly IReadOnlyDictionary<string, IEncryptor> _encryptors;
    private readonly IReadOnlyDictionary<string, IHasher> _digests;
    private readonly IReadOnlyDictionary<string, IMacProvider> _macs;

    public CryptoProviderFactory(
        IEnumerable<IPasswordHasher> hashers,
        IEnumerable<ISigner> signers,
        IEnumerable<IEncryptor> encryptors,
        IEnumerable<IHasher> hashers2,
        IEnumerable<IMacProvider> macs)
    {
        _hashers = hashers.ToDictionary(h => h.Algorithm, StringComparer.OrdinalIgnoreCase);
        _signers = signers.ToDictionary(s => s.Algorithm, StringComparer.OrdinalIgnoreCase);
        _encryptors = encryptors.ToDictionary(e => e.Algorithm, StringComparer.OrdinalIgnoreCase);
        _digests = hashers2.ToDictionary(h => h.Algorithm, StringComparer.OrdinalIgnoreCase);
        _macs = macs.ToDictionary(m => m.Algorithm, StringComparer.OrdinalIgnoreCase);
    }

    public IPasswordHasher GetPasswordHasher(string algorithm) =>
        _hashers.TryGetValue(algorithm, out var h)
            ? h
            : throw new NotSupportedException($"未注册的密码哈希算法：{algorithm}");

    public ISigner GetSigner(string algorithm) =>
        _signers.TryGetValue(algorithm, out var s)
            ? s
            : throw new NotSupportedException($"未注册的签名算法：{algorithm}");

    public IEncryptor GetEncryptor(string algorithm) =>
        _encryptors.TryGetValue(algorithm, out var e)
            ? e
            : throw new NotSupportedException($"未注册的加密算法：{algorithm}");

    public IMacProvider GetMacProvider(string algorithm) =>
    _macs.TryGetValue(algorithm, out var m)
        ? m
        : throw new NotSupportedException($"未注册的 MAC 算法：{algorithm}");

    public IHasher GetHasher(string algorithm) =>
        _digests.TryGetValue(algorithm, out var h)
            ? h
            : throw new NotSupportedException($"未注册的摘要算法：{algorithm}");
}
