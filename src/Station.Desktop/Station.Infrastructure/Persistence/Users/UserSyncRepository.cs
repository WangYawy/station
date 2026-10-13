using Station.Data.SqlSugar;
using Station.Domain.Users;

namespace Station.Infrastructure.Persistence.Users;

public sealed class UserSyncRepository : IUserSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public UserSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public async Task<User?> GetByUserNoAsync(
        string userNo, bool onlyActive = false, CancellationToken ct = default)
    {
        var query = _db.Queryable<UserPo>().Where(x => x.UserNo == userNo);
        if (onlyActive) query = query.Where(x => x.IsActive);

        var po = await query.FirstAsync();
        return po is null ? null : UserMapper.ToDomain(po);
    }

    public async Task<IReadOnlyList<User>> GetListAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<UserPo>().ToListAsync();
        return list.Select(UserMapper.ToDomain).ToList();
    }

    public Task InsertAsync(User user, CancellationToken ct = default)
        => _db.Insertable(UserMapper.ToPo(user)).ExecuteCommandAsync();

    public Task<int> UpdateAsync(User user, CancellationToken ct = default)
    => _db.Updateable(UserMapper.ToPo(user)).ExecuteCommandAsync();

    public Task UpdateRangeAsync(IEnumerable<User> users, CancellationToken ct = default)
        => _db.Updateable(users.Select(UserMapper.ToPo).ToList()).ExecuteCommandAsync();
}
