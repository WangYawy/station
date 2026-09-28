namespace Station.Crypto;

/// <summary>加密模块基础异常。</summary>
public class CryptoException : Exception
{
    public CryptoException(string message) : base(message) { }
    public CryptoException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>解密失败（密钥错误 / 密文损坏 / AAD 不匹配）。</summary>
public sealed class DecryptionFailedException : CryptoException
{
    public DecryptionFailedException(string message) : base(message) { }
    public DecryptionFailedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 主密钥未找到（版本不存在 / 文件缺失）。
/// 命名与 BCL 的 System.Collections.Generic.KeyNotFoundException 区分。
/// </summary>
public sealed class MasterKeyNotFoundException : CryptoException
{
    public MasterKeyNotFoundException(string message) : base(message) { }
}

/// <summary>密文格式错误（版本前缀缺失 / Base64 解析失败）。</summary>
public sealed class CipherFormatException : CryptoException
{
    public CipherFormatException(string message) : base(message) { }
    public CipherFormatException(string message, Exception inner) : base(message, inner) { }
}
