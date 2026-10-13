using Station.Data.Repositories;
using Station.Domain.Accounts;

namespace Station.Infrastructure.Persistence.Accounts;

public sealed class AccountRepository : IAccountRepository
{
    private readonly IUnitOfWork _uow;

    public AccountRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<Account?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        var po = await repo.FirstAsync(x => x.Id == id && !x.IsDeleted, ct);
        return po is null ? null : AccountMapper.ToDomain(po);
    }

    public async Task<IReadOnlyList<Account>> GetByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        var list = await repo.GetListAsync(x => x.UserId == userId && !x.IsDeleted, ct);
        return list.Select(AccountMapper.ToDomain).ToList();
    }

    public async Task<Account?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        var po = await repo.FirstAsync(x => x.UserName == userName && !x.IsDeleted, ct);
        return po is null ? null : AccountMapper.ToDomain(po);
    }

    public Task<bool> ExistsUserNameAsync(string userName, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        return repo.IsAnyAsync(x => x.UserName == userName && !x.IsDeleted, ct);
    }

    public async Task AddAsync(Account account, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        var po = AccountMapper.ToPo(account);
        await repo.InsertAsync(po, ct);
    }

    public async Task UpdateAsync(Account account, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AccountPo>();
        var po = AccountMapper.ToPo(account);
        await repo.UpdateAsync(po, ct);
    }

    public async Task SoftDeleteAsync(Account account, CancellationToken ct = default)
    {
        // 软删除同时改写用户名，需要落库最新值
        await UpdateAsync(account, ct);
    }
}
