using Station.Data.Repositories;
using Station.Domain.Licensing;

namespace Station.Infrastructure.Persistence.Licensing;

public sealed class LicenseRepository : ILicenseRepository
{
    private readonly IUnitOfWork _uow;
    public LicenseRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<License?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<LicensePo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : LicenseMapper.ToDomain(po);
    }

    public async Task<License?> GetActiveAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<LicensePo>();
        var po = await repo.FirstAsync(x => x.IsActive, ct);
        return po is null ? null : LicenseMapper.ToDomain(po);
    }

    public async Task<License?> GetByKeyAsync(string licenseKey, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<LicensePo>();
        var po = await repo.FirstAsync(x => x.LicenseKey == licenseKey, ct);
        return po is null ? null : LicenseMapper.ToDomain(po);
    }

    public Task AddAsync(License license, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<LicensePo>();
        return repo.InsertAsync(LicenseMapper.ToPo(license), ct);
    }

    public Task UpdateAsync(License license, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<LicensePo>();
        return repo.UpdateAsync(LicenseMapper.ToPo(license), ct);
    }
}
