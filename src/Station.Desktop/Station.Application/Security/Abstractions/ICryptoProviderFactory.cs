using System.ComponentModel.Design;
using Station.Domain.Security;

namespace Station.Application.Security.Abstractions;

/// <summary>
/// 算法实现工厂：按算法标识返回具体 provider。内部字典缓存，O(1) 命中。
/// </summary>
public interface ICryptoProviderFactory
{
    /// <summary>获取密码哈希器。</summary>
    IPasswordHasher GetPasswordHasher(string algorithm);

    /// <summary>获取签名器。</summary>
    ISigner GetSigner(string algorithm);

    /// <summary>获取对称加解密器。</summary>
    IEncryptor GetEncryptor(string algorithm);

    /// <summary>获取摘要器（SM3 / SHA-256）。</summary>
    IHasher GetHasher(string algorithm);

    /// <summary>获取对称 MAC 提供者。</summary>
    IMacProvider GetMacProvider(string algorithm);
}
