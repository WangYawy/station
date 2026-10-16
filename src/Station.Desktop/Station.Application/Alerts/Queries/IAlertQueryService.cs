using Station.Application.Alerts.Dtos;
using Station.Contracts;
using Station.Data.Paging;

namespace Station.Application.Alerts;

public interface IAlertQueryService
{
    Task<PageResult<AlertDto>> PageAsync(
        AlertLevel? level, AlertStatus? status, int count,
        CancellationToken ct = default);

    Task<int> CountPendingAsync(CancellationToken ct = default);
}
