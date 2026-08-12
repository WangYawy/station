using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;

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
}
