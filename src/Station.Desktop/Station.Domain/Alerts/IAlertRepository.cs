namespace Station.Domain.Alerts;

public interface IAlertRepository
{
    Task<Alert?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<int> CountPendingAsync(CancellationToken ct = default);
    Task AddAsync(Alert alert, CancellationToken ct = default);
    Task UpdateAsync(Alert alert, CancellationToken ct = default);
}
