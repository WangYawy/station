namespace Station.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<User?> GetByUserNoAsync(string userNo, CancellationToken ct = default);
    Task<bool> ExistsUserNoAsync(string userNo, CancellationToken ct = default);
    Task<bool> ExistsActiveInDeptAsync(long deptId, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetListAsync(CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}
