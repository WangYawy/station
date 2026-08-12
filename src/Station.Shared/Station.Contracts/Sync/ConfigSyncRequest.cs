namespace Station.Contracts.Sync;

/// <summary>
/// 配置同步请求：携带各配置表本地已应用版本号；
/// 空字典或缺少条目时平台返回该表全量数据。
/// </summary>
public sealed record ConfigSyncRequest
{
    public long StationId { get; init; }

    public IReadOnlyDictionary<string, long> AppliedVersions { get; init; } = new Dictionary<string, long>();
}
