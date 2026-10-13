namespace Station.Domain.Departments;

public interface IDeptRepository
{
    Task<Dept?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Dept?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(long parentId, CancellationToken ct = default);

    Task<IReadOnlyList<Dept>> GetActiveListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Dept>> GetActiveChildrenAsync(long parentId, CancellationToken ct = default);

    Task AddAsync(Dept dept, CancellationToken ct = default);
    Task UpdateAsync(Dept dept, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
