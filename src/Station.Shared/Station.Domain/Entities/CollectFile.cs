using SqlSugar;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 采集文件记录：任务内每个待采集文件的元数据与采集状态。
/// Fingerprint = 文件名|大小|修改时间，用于"已采集文件自动跳过"。
/// </summary>
[SugarTable("station_collect_file")]
[SugarIndex("idx_collect_file_task", nameof(TaskId), OrderByType.Asc)]
public sealed class CollectFile
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TaskId { get; set; }

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Extension { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }

    [SugarColumn(Length = 128)]
    public string Fingerprint { get; set; } = string.Empty;

    public CollectFileStatus Status { get; set; }

    /// <summary>0~1。</summary>
    public double Progress { get; set; }

    public double SpeedBytesPerSecond { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? ErrorMessage { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CollectedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalModifiedAt { get; set; }

    public UploadStatus SyncStatus { get; set; } = UploadStatus.Pending;

    public double UploadProgress { get; set; }

    public double UploadSpeedBytesPerSecond { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UploadError { get; set; }

    public int UploadRetryCount { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? RemotePath { get; set; }

    /// <summary>采集后本地缓存文件 SM3（"采集即校验"元数据）。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Sm3 { get; set; }

    /// <summary>文件业务编号 = {采集站编号}-{本地文件ID}。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? FileNo { get; set; }
}
