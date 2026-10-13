namespace Station.Domain.Accounts;

/// <summary>
/// 账号同步专用仓储（长连接、非事务场景）。
/// 供平台快照应用使用，不参与业务 UoW 事务。
/// </summary>
public interface IAccountLoopRepository
{
    /// <summary>获取所有未软删除账号。</summary>
    Task<IReadOnlyList<Account>> GetListAsync(CancellationToken ct = default);

    Task<int> InsertAsync(Account account, CancellationToken ct = default);

    Task<int> UpdateRangeAsync(IEnumerable<Account> accounts, CancellationToken ct = default);
}
