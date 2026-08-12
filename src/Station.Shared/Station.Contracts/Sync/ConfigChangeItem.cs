namespace Station.Contracts.Sync;

/// <summary>单条配置变更（增量同步的最小单元）。</summary>
public sealed record ConfigChangeItem
{
    /// <summary>实体类型：Dept / User / Account / Recorder / StationPolicy / Whitelist。</summary>
    public required string EntityType { get; init; }

    public SyncOperation Operation { get; init; }

    /// <summary>实体 JSON 载荷（按 EntityType 反序列化）。</summary>
    public required string PayloadJson { get; init; }

    /// <summary>该实体类型下的单调递增版本号。</summary>
    public long Version { get; init; }
}
