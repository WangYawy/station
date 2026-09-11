using SqlSugar;
using Station.Contracts;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 采集任务：一次记录仪连接对应一个任务。
/// 记录 扫描/采集 进度、速度、操作人、任务结果；同步状态由上传阶段补充。
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

    /// <summary>
    /// 记录仪名称
    /// </summary>
    [SugarColumn(Length = 64)]
    public string RecorderName { get; set; } = string.Empty;

    /// <summary>
    /// 记录仪序列号
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? RecorderSerial { get; set; }

    /// <summary>接入协议（ProtocolType）。</summary>
    public ProtocolType Protocol { get; set; }

    /// <summary>设备根路径（UMS=盘符根，MTP=MTP://PnP）；多设备/混合协议时按任务路由读取。</summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string SourceRoot { get; set; } = string.Empty;

    /// <summary>
    /// 操作人Id
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? OperatorUserId { get; set; }
    /// <summary>
    /// 操作人部门Id
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }
    /// <summary>
    /// 采集任务状态
    /// </summary>
    public CollectTaskStatus Status { get; set; }

    /// <summary>是否自动触发（记录仪接入即采）。</summary>
    public bool IsAuto { get; set; }

    /// <summary>紧急优先：操作员在卡片上直接标记（上限由 CollectOptions.MaxEmergencyTasks 控制）。</summary>
    public bool IsEmergency { get; set; }
    /// <summary>
    /// 文件总数量
    /// </summary>
    public int TotalFiles { get; set; }
    /// <summary>
    /// 已采集文件数量
    /// </summary>
    public int CollectedFiles { get; set; }
    /// <summary>
    /// 跳过文件数量
    /// </summary>
    public int SkippedFiles { get; set; }
    /// <summary>
    /// 失败文件数量
    /// </summary>
    public int FailedFiles { get; set; }
    /// <summary>
    /// 文件总大小
    /// </summary>
    public long TotalBytes { get; set; }
    /// <summary>
    /// 已采集文件大小
    /// </summary>
    public long CollectedBytes { get; set; }
    /// <summary>
    /// 每秒字节数
    /// </summary>
    public double SpeedBytesPerSecond { get; set; }
    /// <summary>
    /// 采集任务开始时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }
    /// <summary>
    /// 采集任务完成时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAt { get; set; }
    /// <summary>
    /// 采集任务错误信息
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// 采集任务上传状态
    /// </summary>
    public UploadStatus SyncStatus { get; set; } = UploadStatus.Pending;
    /// <summary>
    /// 已上传文件数量
    /// </summary>
    public int UploadedFiles { get; set; }
    /// <summary>
    /// 已上传文件大小
    /// </summary>
    public long UploadedBytes { get; set; }
    /// <summary>
    /// 上传错误信息
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UploadError { get; set; }

    /// <summary>台账/元数据上报处理时间（空=未处理）。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LedgeredAt { get; set; }
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
