namespace Station.Domain.Recorders;

public interface IRecorderRepository
{
    Task<Recorder?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Recorder?> GetBySerialAsync(string serialNumber, CancellationToken ct = default);
    Task<Recorder?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Recorder?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<bool> ExistsSerialAsync(string serialNumber, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Recorder>> GetActiveListAsync(CancellationToken ct = default);
    Task AddAsync(Recorder recorder, CancellationToken ct = default);
    Task UpdateAsync(Recorder recorder, CancellationToken ct = default);
}
