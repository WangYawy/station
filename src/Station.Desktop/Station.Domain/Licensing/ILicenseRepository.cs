namespace Station.Domain.Licensing;

public interface ILicenseRepository
{
    Task<License?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<License?> GetActiveAsync(CancellationToken ct = default);
    Task<License?> GetByKeyAsync(string licenseKey, CancellationToken ct = default);
    Task AddAsync(License license, CancellationToken ct = default);
    Task UpdateAsync(License license, CancellationToken ct = default);
}
