using SqlSugar;
using Station.Contracts;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Domain.Entities;

/// <summary>
/// 视频/音视图/日志文件台账。文件业务编号 = {采集站编号}-{本地文件ID}；
/// 本地记录不物理删除，保证本地 ID 不复用。
/// </summary>
[SugarTable("station_video_file")]
[SugarIndex("uk_videofile_fileno", nameof(FileNo), OrderByType.Asc, true)]
public sealed class VideoFile
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>采集站本地文件 ID（跨端复合引用的组成之一）。</summary>
    public long LocalFileId { get; set; }

    /// <summary>文件业务编号（跨端引用）。</summary>
    [SugarColumn(Length = 64)]
    public string FileNo { get; set; } = string.Empty;

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public FileKind Kind { get; set; }

    [SugarColumn(Length = 64)]
    public string Sm3 { get; set; } = string.Empty;

    /// <summary>采集时间（采集站系统时间）。</summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>记录仪文件原始时间，单独存储、不覆盖。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalTime { get; set; }

    /// <summary>归属用户（人员）ID。</summary>
    [SugarColumn(IsNullable = true)]
    public long? UserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public TaskStatus Status { get; set; }
}
