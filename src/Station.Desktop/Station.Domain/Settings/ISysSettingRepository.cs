namespace Station.Domain.Settings;

public interface ISysSettingRepository
{
    Task<IReadOnlyList<SysSetting>> GetByGroupAsync(string groupKey, CancellationToken ct = default);
    Task<SysSetting?> GetAsync(string groupKey, string subKey, CancellationToken ct = default);

    Task<IReadOnlyList<SysSetting>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(SysSetting setting, CancellationToken ct = default);
    Task UpsertRangeAsync(IEnumerable<SysSetting> settings, CancellationToken ct = default);
}
