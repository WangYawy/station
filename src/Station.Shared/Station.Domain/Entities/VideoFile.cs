using Station.Contracts;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Domain.Entities;

/// <summary>
/// 视音频文件元数据。文件业务编号 = {采集站编号}-{本地文件ID}；
/// 本地记录不物理删除，保证本地 ID 不复用。
/// </summary>
public sealed class VideoFile
{
    public long Id { get; set; }

    /// <summary>采集站本地 ID（跨端复合引用的组成之一）。</summary>
    public long LocalFileId { get; set; }

    /// <summary>文件业务编号（跨端引用）。</summary>
    public required string FileNo { get; set; }

    public required string FileName { get; set; }

    public long Size { get; set; }

    public FileKind Kind { get; set; }

    public required string Sm3 { get; set; }

    /// <summary>采集时间（采集站系统时间）。</summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>记录仪文件原始时间，单独存储、不覆盖。</summary>
    public DateTime? OriginalTime { get; set; }

    /// <summary>归属用户（人员）ID。</summary>
    public long? UserId { get; set; }

    public long? DeptId { get; set; }

    public TaskStatus Status { get; set; }
}
