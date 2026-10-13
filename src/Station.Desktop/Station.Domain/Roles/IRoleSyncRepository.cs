namespace Station.Domain.Roles;

public interface IRoleSyncRepository
{
    Task<IReadOnlyList<Role>> GetListAsync(CancellationToken ct = default);
    Task InsertAsync(Role role, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<Role> roles, CancellationToken ct = default);
}
