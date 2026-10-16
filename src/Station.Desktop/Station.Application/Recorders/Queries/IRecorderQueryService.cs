using Station.Application.Recorders.Dtos;

namespace Station.Application.Recorders;

public interface IRecorderQueryService
{
    Task<RecorderListItemDto?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<RecorderListItemDto?> GetBySerialAsync(string serialNumber, CancellationToken ct = default);
    Task<RecorderListItemDto?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<RecorderListItemDto?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<RecorderListItemDto>> GetListAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<bool> ExistsSerialAsync(string serialNumber, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
}
