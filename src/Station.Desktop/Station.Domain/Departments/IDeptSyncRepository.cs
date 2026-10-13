namespace Station.Domain.Departments;

/// <summary>长连接、非事务场景专用（平台快照同步）。</summary>
public interface IDeptSyncRepository
{
    Task<IReadOnlyList<Dept>> GetListAsync(CancellationToken ct = default);
    Task InsertAsync(Dept dept, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<Dept> depts, CancellationToken ct = default);
}
