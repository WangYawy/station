using Station.Data.IdGeneration;
using Station.Data.Repositories;
using Station.Domain.Users;

namespace Station.Infrastructure.Persistence.Users;

public sealed class UserRoleRepository : IUserRoleRepository
{
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;

    public UserRoleRepository(IUnitOfWork uow, IIdGenerator ids)
    {
        _uow = uow;
        _ids = ids;
    }

    public async Task<IReadOnlyList<long>> GetRoleIdsByUserIdAsync(
        long userId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserRolePo>();
        var list = await repo.GetListAsync(x => x.UserId == userId, ct);
        return list.Select(x => x.RoleId).ToList();
    }

    public Task DeleteByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserRolePo>();
        return repo.DeleteAsync(x => x.UserId == userId, ct);
    }

    public Task DeleteByRoleIdAsync(long roleId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserRolePo>();
        return repo.DeleteAsync(x => x.RoleId == roleId, ct);
    }

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserRolePo>();
        return repo.DeleteAsync(_ => true, ct);
    }

    public async Task AddAsync(long userId, long roleId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<UserRolePo>();
        await repo.InsertAsync(new UserRolePo
        {
            Id = _ids.NewId(),
            UserId = userId,
            RoleId = roleId
        }, ct);
    }

    public async Task AddRangeAsync(
        long userId, IReadOnlyList<long> roleIds, CancellationToken ct = default)
    {
        var distinct = roleIds.Distinct().ToList();
        if (distinct.Count == 0) return;

        var links = distinct.Select(rid => new UserRolePo
        {
            Id = _ids.NewId(),
            UserId = userId,
            RoleId = rid
        }).ToList();

        var repo = _uow.GetRepository<UserRolePo>();
        await repo.InsertRangeAsync(links, ct);
    }

    public async Task AddRangeBatchAsync(
        IReadOnlyList<(long UserId, long RoleId)> links, CancellationToken ct = default)
    {
        if (links.Count == 0) return;

        var pos = links.Select(l => new UserRolePo
        {
            Id = _ids.NewId(),
            UserId = l.UserId,
            RoleId = l.RoleId
        }).ToList();

        var repo = _uow.GetRepository<UserRolePo>();
        await repo.InsertRangeAsync(pos, ct);
    }
}
