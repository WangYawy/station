namespace Station.Application.PlatformSync;

/// <summary>配置同步状态（进程内）：各 EntityType 已应用版本 + 最近同步时间。</summary>
public interface IConfigSyncState
{
    IReadOnlyDictionary<string, long> AppliedVersions { get; }

    DateTime? LastSyncAt { get; }

    void RecordApplied(string entityType, long version);

    void MarkSynced();
}

public sealed class ConfigSyncState : IConfigSyncState
{
    private readonly Dictionary<string, long> _applied = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> AppliedVersions => _applied;

    public DateTime? LastSyncAt { get; private set; }

    public void RecordApplied(string entityType, long version)
    {
        _applied[entityType] = version;
    }

    public void MarkSynced() => LastSyncAt = DateTime.Now;
}
