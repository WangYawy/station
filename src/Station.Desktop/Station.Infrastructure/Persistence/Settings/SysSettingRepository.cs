using SqlSugar;
using Station.Domain.Settings;

namespace Station.Infrastructure.Persistence.Settings;

public sealed class SysSettingRepository : ISysSettingRepository
{
    private readonly ISqlSugarClient _db;
    public SysSettingRepository(ISqlSugarClient db) => _db = db;

    public async Task<IReadOnlyList<SysSetting>> GetByGroupAsync(string groupKey, CancellationToken ct = default)
    {
        var list = await _db.Queryable<SysSettingPo>()
            .Where(x => x.GroupKey == groupKey).ToListAsync(ct);
        return list.Select(SysSettingMapper.ToDomain).ToList();
    }

    public async Task<SysSetting?> GetAsync(string groupKey, string subKey, CancellationToken ct = default)
    {
        var po = await _db.Queryable<SysSettingPo>()
            .Where(x => x.GroupKey == groupKey && x.SubKey == subKey).FirstAsync(ct);
        return po is null ? null : SysSettingMapper.ToDomain(po);
    }

    public async Task<IReadOnlyList<SysSetting>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Queryable<SysSettingPo>().Select(x => SysSettingMapper.ToDomain(x)).ToListAsync();
    }

    public async Task UpsertAsync(SysSetting setting, CancellationToken ct = default)
    {
        var po = SysSettingMapper.ToPo(setting);
        var exists = await _db.Queryable<SysSettingPo>()
            .Where(x => x.GroupKey == po.GroupKey && x.SubKey == po.SubKey).AnyAsync(ct);

        if (exists)
        {
            po.Version += 1;
            await _db.Updateable(po)
                .UpdateColumns(x => new { x.ValueJson, x.ValueType, x.IsEncrypted, x.Version, x.UpdatedAt, x.UpdatedBy, x.Remark })
                .ExecuteCommandAsync(ct);
        }
        else
        {
            await _db.Insertable(po).ExecuteCommandAsync(ct);
        }
    }

    public async Task UpsertRangeAsync(IEnumerable<SysSetting> settings, CancellationToken ct = default)
    {
        foreach (var s in settings) await UpsertAsync(s, ct);
    }
}
