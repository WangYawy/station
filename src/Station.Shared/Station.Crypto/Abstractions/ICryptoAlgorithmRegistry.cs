namespace Station.Crypto;

/// <summary>
/// 算法注册表：算法标识 → 实现实例。
///
/// 【唯一 switch】所有算法解析必须走此接口，禁止散落 switch 表达式。
/// 实现：Station.Crypto.Engine.Internal.AlgorithmRegistry。
///
/// ⚠️ 返回的实例建议无状态、线程安全。
/// </summary>
public interface ICryptoAlgorithmRegistry
{
    /// <exception cref="NotSupportedException">算法未注册。</exception>
    IPasswordHasher GetPasswordHasher(string algorithm);

    /// <exception cref="NotSupportedException">算法未注册。</exception>
    ISigner GetSigner(string algorithm);

    /// <exception cref="NotSupportedException">算法未注册。</exception>
    IEncryptor GetEncryptor(string algorithm, IMasterKeyProvider keys);

    /// <exception cref="NotSupportedException">算法未注册。</exception>
    IHasher GetHasher(string algorithm);

    /// <exception cref="NotSupportedException">算法未注册。</exception>
    IMacProvider GetMacProvider(string algorithm);
}
