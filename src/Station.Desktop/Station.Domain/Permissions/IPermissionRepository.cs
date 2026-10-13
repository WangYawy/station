namespace Station.Domain.Permissions;

public interface IPermissionRepository
{
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task<Permission?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<Permission>> GetListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetActiveListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task AddAsync(Permission permission, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default);
    Task UpdateAsync(Permission permission, CancellationToken ct = default);
}
