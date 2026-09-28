namespace Station.Crypto;

/// <summary>
/// 对称加解密抽象。
/// 密文格式：v{n}:base64(iv || ciphertext || tag)。
/// </summary>
public interface IEncryptor
{
    /// <summary>算法标识（CryptoAlgorithm 常量）。</summary>
    string Algorithm { get; }

    /// <summary>
    /// 加密。
    /// </summary>
    /// <param name="plaintext">明文（UTF-8）。</param>
    /// <param name="aad">附加认证数据，用于绑定上下文（如字段名），防密文替换。可为 null。</param>
    /// <returns>密文（v{n}:base64 格式）。</returns>
    string Encrypt(string plaintext, string? aad = null);

    /// <summary>
    /// 解密。aad 必须与加密时一致，否则校验失败。
    /// </summary>
    /// <exception cref="DecryptionFailedException">密钥错误 / 密文损坏 / AAD 不匹配。</exception>
    /// <exception cref="CipherFormatException">密文格式错误。</exception>
    string Decrypt(string ciphertext, string? aad = null);
}
