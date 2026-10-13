namespace Station.Domain.Accounts;

/// <summary>账号聚合专用仓储。查询默认过滤已软删除记录。</summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Account>> GetByUserIdAsync(long userId, CancellationToken ct = default); 

    Task<Account?> GetByUserNameAsync(string userName, CancellationToken ct = default);

    Task<bool> ExistsUserNameAsync(string userName, CancellationToken ct = default);

    Task AddAsync(Account account, CancellationToken ct = default);

    Task UpdateAsync(Account account, CancellationToken ct = default);

    Task SoftDeleteAsync(Account account, CancellationToken ct = default);
}
