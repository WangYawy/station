namespace Station.Application.Security;

/// <summary>
/// 密码服务门面：所有密码哈希相关的业务动作统一入口。
/// 
/// 【设计原则】
///   1) 唯一入口：注册/重置/改密/登录/校验，全部走本接口，
///      禁止业务层直接依赖 ICryptoPolicyService / ICryptoProviderFactory / IPasswordHasher；
///   2) 算法一致：写密码（HashAsync）和验密码（VerifyAsync）都按策略表当前算法执行，
///      结构上保证"注册的密码一定能登录"；
///   3) 迁移内聚：legacy 回退与惰性重哈希逻辑集中在本接口实现内，
///      业务代码只需根据 NeedsRehash 决定何时写库。
/// 
/// 【适用边界】
///   只服务 CryptoUsage.Password 用途。License/文件摘要/配置字段加密语义不同，
///   请使用各自的扩展方法或服务，不要复用本接口。
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// 生成密码哈希（按当前策略算法）。
    /// 用于注册、管理员重置密码、用户改密等所有"写"场景。
    /// </summary>
    /// <param name="password">明文密码，不能为空。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>形如 "sm3$10000$&lt;salt&gt;$&lt;hash&gt;" 的哈希字符串（含算法标识）。</returns>
    /// <exception cref="ArgumentException">密码为空。</exception>
    Task<string> HashAsync(string password, CancellationToken ct = default);

    /// <summary>
    /// 校验密码。内含：
    ///   1) 按当前策略算法校验；
    ///   2) 失败后按策略表的 LegacyAlgorithms 列表依次回退；
    ///   3) 返回 NeedsRehash 标志（调用方决定何时用 HashAsync 重写）。
    /// 
    /// 【注意】本方法不直接写库。惰性重哈希由调用方完成，
    /// 便于在事务中或事务外灵活处理。
    /// </summary>
    /// <param name="password">待校验的明文密码。</param>
    /// <param name="storedHash">数据库中存储的哈希字符串。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PasswordVerifyResult> VerifyAsync(
        string password,
        string storedHash,
        CancellationToken ct = default);
}

/// <summary>
/// 密码校验结果。
/// </summary>
/// <param name="Ok">是否校验通过。</param>
/// <param name="NeedsRehash">
/// 是否需要重哈希。true 表示当前存储哈希的算法或参数已过期，
/// 调用方应使用 HashAsync 生成新哈希并写回。
/// </param>
/// <param name="UsedLegacyAlgorithm">
/// 命中的 legacy 算法标识（如 "MD5"）；null 表示命中的是当前算法或无 legacy。
/// 仅用于日志/审计。
/// </param>
public sealed record PasswordVerifyResult(
    bool Ok,
    bool NeedsRehash,
    string? UsedLegacyAlgorithm);
