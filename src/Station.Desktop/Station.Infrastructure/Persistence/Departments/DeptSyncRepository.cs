using Station.Data.SqlSugar;   // 按你项目实际 ILoopSqlSugarClient 所在命名空间调整
using Station.Domain.Departments;

namespace Station.Infrastructure.Persistence.Departments;

public sealed class DeptSyncRepository : IDeptSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public DeptSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public async Task<IReadOnlyList<Dept>> GetListAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<DeptPo>().ToListAsync();
        return list.Select(DeptMapper.ToDomain).ToList();
    }

    public Task InsertAsync(Dept dept, CancellationToken ct = default)
        => _db.Insertable(DeptMapper.ToPo(dept)).ExecuteCommandAsync();

    public Task UpdateRangeAsync(IEnumerable<Dept> depts, CancellationToken ct = default)
        => _db.Updateable(depts.Select(DeptMapper.ToPo).ToList()).ExecuteCommandAsync();
}
