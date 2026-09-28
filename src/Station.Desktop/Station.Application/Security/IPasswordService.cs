using Station.Crypto;

namespace Station.Application.Security;

/// <summary>
/// 密码服务门面：注册/重置/改密/登录校验统一入口。
/// </summary>
public interface IPasswordService
{
    /// <summary>生成密码哈希（按当前策略）。</summary>
    Task<string> HashAsync(string password, CancellationToken ct = default);

    /// <summary>校验密码（含 legacy 回退，返回是否需重哈希）。</summary>
    Task<PasswordVerifyResult> VerifyAsync(
        string password, string storedHash, CancellationToken ct = default);
}
