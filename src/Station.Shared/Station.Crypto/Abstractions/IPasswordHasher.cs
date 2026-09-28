namespace Station.Crypto;

/// <summary>密码哈希算法抽象。所有实现无状态、线程安全。</summary>
public interface IPasswordHasher
{
    /// <summary>算法标识（如 "PBKDF2-HMAC-SM3"）。</summary>
    string Algorithm { get; }

    /// <summary>生成密码哈希字符串（含算法前缀、盐、迭代次数）。</summary>
    string Hash(string password);

    /// <summary>校验密码（固定时间比较）。</summary>
    bool Verify(string password, string storedHash);

    /// <summary>判断已存储的哈希是否需要按当前算法/参数重哈希。</summary>
    bool NeedsRehash(string storedHash);
}
