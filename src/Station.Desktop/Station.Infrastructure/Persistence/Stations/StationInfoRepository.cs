using SqlSugar;
using Station.Domain.Stations;

namespace Station.Infrastructure.Persistence.Stations;

public sealed class StationInfoRepository : IStationInfoRepository
{
    private readonly ISqlSugarClient _db;
    public StationInfoRepository(ISqlSugarClient db) => _db = db;

    public async Task<StationInfo?> GetAsync(CancellationToken ct = default)
    {
        var po = await _db.Queryable<StationInfoPo>().FirstAsync(ct);
        return po is null ? null : StationInfoMapper.ToDomain(po);
    }

    public async Task UpsertAsync(StationInfo info, CancellationToken ct = default)
    {
        var po = StationInfoMapper.ToPo(info);
        // 单行表：存在即 Update，不存在即 Insert
        var exists = await _db.Queryable<StationInfoPo>()
            .Where(x => x.Id == po.Id).AnyAsync(ct);

        if (exists) await _db.Updateable(po).ExecuteCommandAsync(ct);
        else await _db.Insertable(po).ExecuteCommandAsync(ct);
    }
}
