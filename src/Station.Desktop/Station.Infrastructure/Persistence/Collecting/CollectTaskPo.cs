using SqlSugar;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

[SugarTable("station_collect_task")]
[SugarIndex("idx_collect_task_status", nameof(Status), OrderByType.Asc)]
[SugarIndex("idx_collect_task_created", nameof(CreatedAt), OrderByType.Desc)]
public sealed class CollectTaskPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 128)]
    public string TaskNo { get; set; } = string.Empty;

    /// <summary>设备路由标识</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? DeviceKey { get; set; }
    public long RecorderId { get; set; }
    [SugarColumn(Length = 64)]
    public string RecorderCode { get; set; } = string.Empty;
    [SugarColumn(Length = 64)]
    public string RecorderName { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? RecorderSerial { get; set; }
    public ProtocolType Protocol { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? SourceRoot { get; set; }
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? PhysicalPortId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? OperatorUserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public CollectTaskStatus Status { get; set; }

    public bool IsAuto { get; set; }

    public bool IsEmergency { get; set; }

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

    [SugarColumn(IsNullable = true)]
    public DateTime? LedgeredAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
