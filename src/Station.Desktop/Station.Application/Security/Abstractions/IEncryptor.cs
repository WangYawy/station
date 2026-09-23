namespace Station.Application.Security.Abstractions;

/// <summary>
/// 对称加解密抽象。密文格式：v{密钥版本}:base64(iv || ciphertext || tag)。
/// 密钥从 IMasterKeyProvider 获取，支持在线轮换。
/// </summary>
public interface IEncryptor
{
    /// <summary>算法标识。</summary>
    string Algorithm { get; }

    /// <summary>加密。aad 用于绑定上下文（如字段名），防密文替换。</summary>
    string Encrypt(string plaintext, string? aad = null);

    /// <summary>解密。aad 必须与加密时一致。</summary>
    string Decrypt(string ciphertext, string? aad = null);
}
