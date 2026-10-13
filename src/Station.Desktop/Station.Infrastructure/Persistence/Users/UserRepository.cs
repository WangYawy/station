using Station.Data.Repositories;
using Station.Domain.Users;

namespace Station.Infrastructure.Persistence.Users;

public sealed class UserRepository : IUserRepository
{
    private readonly IUnitOfWork _uow;

    public UserRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : UserMapper.ToDomain(po);
    }

    public async Task<User?> GetByUserNoAsync(string userNo, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        var po = await repo.FirstAsync(x => x.UserNo == userNo, ct);
        return po is null ? null : UserMapper.ToDomain(po);
    }

    public Task<bool> ExistsUserNoAsync(string userNo, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        return repo.IsAnyAsync(x => x.UserNo == userNo, ct);
    }

    public Task<bool> ExistsActiveInDeptAsync(long deptId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        return repo.IsAnyAsync(x => x.DeptId == deptId && x.IsActive, ct);
    }

    public async Task<IReadOnlyList<User>> GetListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        var list = await repo.GetListAsync(null, ct);
        return list.Select(UserMapper.ToDomain).ToList();
    }

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        return repo.InsertAsync(UserMapper.ToPo(user), ct);
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserPo>();
        return repo.UpdateAsync(UserMapper.ToPo(user), ct);
    }
}
