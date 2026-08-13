using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧待下发/已执行指令。</summary>
[SugarTable("platform_command")]
[SugarIndex("idx_platform_cmd_station_status", nameof(StationId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class PlatformCommand
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long StationId { get; set; }

    public CommandType Type { get; set; }

    [SugarColumn(IsNullable = true, Length = 2048)]
    public string? PayloadJson { get; set; }

    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    public DateTime IssuedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ExecutedAt { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? ResultMessage { get; set; }

    [SugarColumn(Length = 128)]
    public string Signature { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 300;
}
