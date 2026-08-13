using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台配置变更（按站发布，版本递增；采集站按已应用版本增量拉取）。</summary>
[SugarTable("platform_config_change")]
[SugarIndex("idx_platform_config_station_version", nameof(StationId), OrderByType.Asc, nameof(Version), OrderByType.Asc)]
public sealed class PlatformConfigChange
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long StationId { get; set; }

    /// <summary>CollectPolicy / StoragePolicy / OperationAuth ...</summary>
    [SugarColumn(Length = 32)]
    public string EntityType { get; set; } = string.Empty;

    public SyncOperation Operation { get; set; }

    [SugarColumn(Length = 2048)]
    public string PayloadJson { get; set; } = string.Empty;

    public long Version { get; set; }

    public DateTime PublishedAt { get; set; }
}
