namespace Station.Crypto;

/// <summary>密码校验结果。</summary>
/// <param name="Ok">是否通过。</param>
/// <param name="NeedsRehash">是否需要按新算法重哈希。</param>
/// <param name="UsedLegacyAlgorithm">命中的旧算法标识（null 表示当前算法）。</param>
public sealed record PasswordVerifyResult(
    bool Ok,
    bool NeedsRehash,
    string? UsedLegacyAlgorithm);
