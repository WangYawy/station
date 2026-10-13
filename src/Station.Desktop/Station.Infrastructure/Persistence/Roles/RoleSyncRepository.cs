using Station.Data.SqlSugar;
using Station.Domain.Roles;

namespace Station.Infrastructure.Persistence.Roles;

public sealed class RoleSyncRepository : IRoleSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public RoleSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public async Task<IReadOnlyList<Role>> GetListAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<RolePo>().ToListAsync();
        return list.Select(RoleMapper.ToDomain).ToList();
    }

    public Task InsertAsync(Role role, CancellationToken ct = default)
        => _db.Insertable(RoleMapper.ToPo(role)).ExecuteCommandAsync();

    public Task UpdateRangeAsync(IEnumerable<Role> roles, CancellationToken ct = default)
        => _db.Updateable(roles.Select(RoleMapper.ToPo).ToList()).ExecuteCommandAsync();
}
