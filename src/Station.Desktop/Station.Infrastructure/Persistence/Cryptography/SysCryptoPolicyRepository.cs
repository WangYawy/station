using SqlSugar;
using Station.Domain.Cryptography;

namespace Station.Infrastructure.Persistence.Cryptography;

public sealed class SysCryptoPolicyRepository : ISysCryptoPolicyRepository
{
    private readonly ISqlSugarClient _db;
    public SysCryptoPolicyRepository(ISqlSugarClient db) => _db = db;

    public async Task<IReadOnlyList<SysCryptoPolicy>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<SysCryptoPolicyPo>().ToListAsync(ct);
        return list.Select(SysCryptoPolicyMapper.ToDomain).ToList();
    }

    public async Task<SysCryptoPolicy?> GetByUsageAsync(string usageCode, CancellationToken ct = default)
    {
        var po = await _db.Queryable<SysCryptoPolicyPo>()
            .Where(x => x.UsageCode == usageCode).FirstAsync(ct);
        return po is null ? null : SysCryptoPolicyMapper.ToDomain(po);
    }

    public async Task UpsertAsync(SysCryptoPolicy policy, CancellationToken ct = default)
    {
        var po = SysCryptoPolicyMapper.ToPo(policy);
        var exists = await _db.Queryable<SysCryptoPolicyPo>()
            .Where(x => x.UsageCode == po.UsageCode).AnyAsync(ct);

        if (exists) await _db.Updateable(po).ExecuteCommandAsync(ct);
        else await _db.Insertable(po).ExecuteCommandAsync(ct);
    }

    public async Task UpsertRangeAsync(IEnumerable<SysCryptoPolicy> policies, CancellationToken ct = default)
    {
        foreach (var p in policies) await UpsertAsync(p, ct);
    }
}
