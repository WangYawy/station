using SqlSugar;
using Station.Domain.Auditing;
using Station.Infrastructure.Persistence.Auditing;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly ISqlSugarClient _db;

    public AuditLogRepository(ISqlSugarClient db) => _db = db;

    public async Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        await _db.Insertable(AuditLogMapper.ToPo(entry)).ExecuteCommandAsync();
    }

    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(
        IReadOnlyCollection<string>? operationTypes,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) return Array.Empty<AuditLog>();

        var q = _db.Queryable<AuditLogPo>();

        if (operationTypes is { Count: > 0 })
        {
            var types = operationTypes.ToList();
            q = q.Where(x => types.Contains(x.OperationType));
        }

        var list = await q
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .Take(take)
            .ToListAsync(ct);

        return list.Select(AuditLogMapper.ToDomain).ToList();
    }
}
