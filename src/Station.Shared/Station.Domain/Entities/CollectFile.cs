using SqlSugar;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 采集文件记录：任务内每个待采集文件的元数据与采集状态。
/// Fingerprint = 。
/// </summary>
[SugarTable("station_collect_file")]
[SugarIndex("idx_collect_file_task", nameof(TaskId), OrderByType.Asc)]
public sealed class CollectFile
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TaskId { get; set; }
    /// <summary>
    /// 文件名称
    /// </summary>
    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;
    /// <summary>
    /// 扩展名
    /// </summary>
    [SugarColumn(Length = 32)]
    public string Extension { get; set; } = string.Empty;
    /// <summary>
    /// 文件相对路径
    /// </summary>
    [SugarColumn(Length = 512)]
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>
    /// 文件大小
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 文件名|大小|修改时间，用于"已采集文件自动跳过"
    /// </summary>
    [SugarColumn(Length = 128)]
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>
    /// 状态
    /// </summary>
    public CollectFileStatus Status { get; set; }

    /// <summary>采集进度 0~1。</summary>
    public double Progress { get; set; }
    /// <summary>
    /// 每秒字节数
    /// </summary>
    public double SpeedBytesPerSecond { get; set; }
    /// <summary>
    /// 上传错误信息
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// 采集时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? CollectedAt { get; set; }
    /// <summary>
    /// 初始修改时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalModifiedAt { get; set; }
    /// <summary>
    /// 文件上传状态
    /// </summary>
    public UploadStatus SyncStatus { get; set; } = UploadStatus.Pending;
    /// <summary>
    /// 文件上传进度
    /// </summary>
    public double UploadProgress { get; set; }
    /// <summary>
    /// 已上传字节数（精确值，用于审计和断点续传）
    /// </summary>
    public long UploadedBytes { get; set; }
    /// <summary>
    /// 文件上传每秒字节数
    /// </summary>
    public double UploadSpeedBytesPerSecond { get; set; }
    /// <summary>
    /// 文件上传错误
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UploadError { get; set; }
    /// <summary>
    /// 文件上传重试次数
    /// </summary>
    public int UploadRetryCount { get; set; }
    /// <summary>
    /// 远端路径
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? RemotePath { get; set; }

    /// <summary>采集后本地缓存文件 SM3（"采集即校验"元数据）。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Sm3 { get; set; }

    /// <summary>文件业务编号 = {采集站编号}-{本地文件ID}。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? FileNo { get; set; }
}
