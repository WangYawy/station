using SqlSugar;
using Station.Domain.Clocks;
using Station.Infrastructure.Persistence.Stations;

namespace Station.Infrastructure.Persistence.Clocks;

public sealed class ClockStateRepository : IClockStateRepository
{
    private readonly ISqlSugarClient _db;
    public ClockStateRepository(ISqlSugarClient db) => _db = db;

    public async Task<ClockState?> GetAsync(CancellationToken ct = default)
    {
        var po = await _db.Queryable<ClockStatePo>().FirstAsync(ct);
        return po is null ? null : ClockStateMapper.ToDomain(po);
    }

    public async Task UpsertAsync(ClockState state, CancellationToken ct = default)
    {
        var po = ClockStateMapper.ToPo(state);
        // 单行表：存在即 Update，不存在即 Insert
        var exists = await _db.Queryable<ClockStatePo>()
            .Where(x => x.Id == po.Id).AnyAsync(ct);

        if (exists) await _db.Updateable(po).ExecuteCommandAsync(ct);
        else await _db.Insertable(po).ExecuteCommandAsync(ct);
    }
}
