using SqlSugar;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 采集任务：一次记录仪连接对应一个任务。
/// 记录 扫描/采集 进度、速度、操作人、任务结果；同步状态由上传阶段（M8）补充。
/// </summary>
[SugarTable("station_collect_task")]
[SugarIndex("idx_collect_task_status", nameof(Status), OrderByType.Asc)]
[SugarIndex("idx_collect_task_created", nameof(CreatedAt), OrderByType.Desc)]
public sealed class CollectTask
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string TaskNo { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string RecorderName { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? RecorderSerial { get; set; }

    /// <summary>接入协议（ProtocolType）。</summary>
    public int Protocol { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? OperatorUserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public CollectTaskStatus Status { get; set; }

    /// <summary>是否自动触发（记录仪接入即采）。</summary>
    public bool IsAuto { get; set; }

    public int TotalFiles { get; set; }

    public int CollectedFiles { get; set; }

    public int SkippedFiles { get; set; }

    public int FailedFiles { get; set; }

    public long TotalBytes { get; set; }

    public long CollectedBytes { get; set; }

    public double SpeedBytesPerSecond { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAt { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? ErrorMessage { get; set; }

    public UploadStatus SyncStatus { get; set; } = UploadStatus.Pending;

    public int UploadedFiles { get; set; }

    public long UploadedBytes { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UploadError { get; set; }

    /// <summary>台账/元数据上报处理时间（空=未处理）。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LedgeredAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
