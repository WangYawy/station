namespace Station.Domain.Cryptography;

public interface ISysCryptoPolicyRepository
{
    Task<IReadOnlyList<SysCryptoPolicy>> GetAllAsync(CancellationToken ct = default);
    Task<SysCryptoPolicy?> GetByUsageAsync(string usageCode, CancellationToken ct = default);
    Task UpsertAsync(SysCryptoPolicy policy, CancellationToken ct = default);
    Task UpsertRangeAsync(IEnumerable<SysCryptoPolicy> policies, CancellationToken ct = default);
}
