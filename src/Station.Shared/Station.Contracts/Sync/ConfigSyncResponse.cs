namespace Station.Contracts.Sync;

/// <summary>配置同步响应：平台侧最新全局版本 + 增量变更列表。</summary>
public sealed record ConfigSyncResponse
{
    public long Version { get; init; }

    public IReadOnlyList<ConfigChangeItem> Changes { get; init; } = [];
}
