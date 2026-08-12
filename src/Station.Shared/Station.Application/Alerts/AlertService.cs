using Station.Domain.Entities;
using Station.Contracts;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using SqlSugar;

namespace Station.Application.Alerts;

public sealed class AlertService : IAlertService
{
    private readonly IRepository<Alert> _alerts;
    private readonly IIdGenerator _idGenerator;

    public AlertService(IRepository<Alert> alerts, IIdGenerator idGenerator)
    {
        _alerts = alerts;
        _idGenerator = idGenerator;
    }

    public async Task WriteAsync(Alert alert)
    {
        alert.Id = _idGenerator.NextId();
        alert.CreatedAt = DateTime.Now;
        await _alerts.InsertAsync(alert);
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        AlertLevel? level,
        AlertStatus? status,
        int count)
    {
        var page = await _alerts.ToPageAsync(
            1,
            count,
            predicate: a =>
                (level == null || a.Level == level) &&
                (status == null || a.Status == status),
            orderBy: a => a.CreatedAt,
            orderType: OrderByType.Desc);
        return page.Items.Select(ToDto).ToList();
    }

    public async Task<int> CountPendingAsync() =>
        await _alerts.CountAsync(a => a.Status == AlertStatus.Pending);

    public async Task<bool> SetStatusAsync(long alertId, AlertStatus status)
    {
        var alert = await _alerts.GetByIdAsync(alertId);
        if (alert is null)
        {
            return false;
        }

        alert.Status = status;
        await _alerts.UpdateAsync(alert);
        return true;
    }

    private static AlertDto ToDto(Alert a) => new(
        a.Id, a.Type, a.Level, a.Status, a.Title, a.Detail, a.Source, a.CreatedAt);
}
