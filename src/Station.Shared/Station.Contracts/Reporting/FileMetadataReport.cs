namespace Station.Contracts.Reporting;

/// <summary>
/// 文件元数据上报（内容文件留在采集站存储目标，平台只收元数据）。
/// 全局唯一性由 (StationId, LocalFileId) 唯一索引保证。
/// </summary>
public sealed record FileMetadataReport
{
    public long StationId { get; init; }

    public long LocalFileId { get; init; }

    /// <summary>文件业务编号 = {采集站编号}-{本地文件ID}。</summary>
    public required string FileNo { get; init; }

    public required string FileName { get; init; }

    public long Size { get; init; }

    public FileKind Kind { get; init; }

    /// <summary>本地采集时计算的 SM3。</summary>
    public required string Sm3 { get; init; }

    /// <summary>采集时间（采集站系统时间）。</summary>
    public DateTime CollectedAt { get; init; }

    /// <summary>记录仪文件原始时间，单独存储不覆盖。</summary>
    public DateTime? OriginalTime { get; init; }

    /// <summary>归属用户工号（跨端自然键，可空=待归属）。</summary>
    public string? UserNo { get; init; }

    public string? DeptCode { get; init; }

    public required string RecorderSerial { get; init; }

    /// <summary>存储位置（目录模板展开后的相对路径，便于平台展示与代理转发定位）。</summary>
    public string? StorageLocation { get; init; }
}
