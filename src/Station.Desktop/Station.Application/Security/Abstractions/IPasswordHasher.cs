namespace Station.Application.Security.Abstractions;

/// <summary>密码哈希算法抽象。所有实现无状态、线程安全。</summary>
public interface IPasswordHasher
{
    /// <summary>算法标识（见 CryptoAlgorithm 常量）。</summary>
    string Algorithm { get; }

    /// <summary>生成密码哈希字符串（含算法前缀、盐、迭代次数）。</summary>
    string Hash(string password);

    /// <summary>校验密码。使用固定时间比较防时序攻击。</summary>
    bool Verify(string password, string storedHash);

    /// <summary>
    /// 判断已存储的哈希是否需要按当前算法/参数重哈希。
    /// 用于惰性迁移：登录成功后若 NeedsRehash=true，立即用新算法重写。
    /// </summary>
    bool NeedsRehash(string storedHash);
}
