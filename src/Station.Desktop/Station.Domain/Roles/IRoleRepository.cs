
namespace Station.Domain.Roles;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<Role>> GetListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetActiveListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetActiveByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task AddAsync(Role role, CancellationToken ct = default);
    Task UpdateAsync(Role role, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
