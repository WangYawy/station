using Station.Data.SqlSugar;
using Station.Domain.Accounts;

namespace Station.Infrastructure.Persistence.Accounts;

public sealed class AccountLoopRepository : IAccountLoopRepository
{
    private readonly ILoopSqlSugarClient _db;

    public AccountLoopRepository(ILoopSqlSugarClient db) => _db = db;

    public async Task<IReadOnlyList<Account>> GetListAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<AccountPo>()
            .Where(x => !x.IsDeleted)
            .ToListAsync();
        return list.Select(AccountMapper.ToDomain).ToList();
    }

    public Task<int> InsertAsync(Account account, CancellationToken ct = default)
        => _db.Insertable(AccountMapper.ToPo(account)).ExecuteCommandAsync();

    public Task<int> UpdateRangeAsync(IEnumerable<Account> accounts, CancellationToken ct = default)
    {
        var pos = accounts.Select(AccountMapper.ToPo).ToList();
        return _db.Updateable(pos).ExecuteCommandAsync();
    }
}
