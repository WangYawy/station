namespace Station.Domain.Users;

public interface IUserRoleRepository
{
    Task<IReadOnlyList<long>> GetRoleIdsByUserIdAsync(long userId, CancellationToken ct = default);

    Task DeleteByUserIdAsync(long userId, CancellationToken ct = default);
    Task DeleteByRoleIdAsync(long roleId, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);

    Task AddAsync(long userId, long roleId, CancellationToken ct = default);
    Task AddRangeAsync(long userId, IReadOnlyList<long> roleIds, CancellationToken ct = default);
    Task AddRangeBatchAsync(IReadOnlyList<(long UserId, long RoleId)> links, CancellationToken ct = default);
}
