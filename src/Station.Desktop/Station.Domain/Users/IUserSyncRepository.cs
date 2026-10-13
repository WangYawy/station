namespace Station.Domain.Users;

public interface IUserSyncRepository
{
    Task<User?> GetByUserNoAsync(string userNo, bool onlyActive = false, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetListAsync(CancellationToken ct = default);
    Task InsertAsync(User user, CancellationToken ct = default);
    
    Task<int> UpdateAsync(User user, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<User> users, CancellationToken ct = default);
}
