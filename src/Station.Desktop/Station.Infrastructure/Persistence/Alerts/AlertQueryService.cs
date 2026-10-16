using SqlSugar;
using Station.Application.Alerts.Dtos;
using Station.Application.Alerts;
using Station.Contracts;
using Station.Data.Paging;

namespace Station.Infrastructure.Persistence.Alerts;

public sealed class AlertQueryService : IAlertQueryService
{
    private readonly ISqlSugarClient _db;

    public AlertQueryService(ISqlSugarClient db) => _db = db;

    public async Task<PageResult<AlertDto>> PageAsync(
        AlertLevel? level, AlertStatus? status, int count, CancellationToken ct = default)
    {
        var q = _db.Queryable<AlertPo>();
        if (level is { } l) q = q.Where(x => x.Level == l);
        if (status is { } s) q = q.Where(x => x.Status == s);

        var list = await q
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .Take(count)
            .Select(x => new AlertDto(
                x.Id, x.Type, x.Level, x.Status,
                x.Title, x.Detail, x.Source, x.CreatedAt))
            .ToListAsync(ct);

        return new PageResult<AlertDto>
        {
            Items = list,
            Total = list.Count,
            PageIndex = 1,
            PageSize = count
        };
    }

    public Task<int> CountPendingAsync(CancellationToken ct = default)
        => _db.Queryable<AlertPo>().Where(x => x.Status == AlertStatus.Pending).CountAsync(ct);
}
