using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧文件元数据（各采集站文件统一列表）。</summary>
[SugarTable("platform_file_metadata")]
[SugarIndex("uk_platform_file_no", nameof(FileNo), OrderByType.Asc, true)]
[SugarIndex("idx_platform_file_station", nameof(StationId), OrderByType.Asc)]
public sealed class PlatformFileMetadata
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long StationId { get; set; }

    public long LocalFileId { get; set; }

    [SugarColumn(Length = 64)]
    public string FileNo { get; set; } = string.Empty;

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public FileKind Kind { get; set; }

    [SugarColumn(Length = 64)]
    public string Sm3 { get; set; } = string.Empty;

    public DateTime CollectedAt { get; set; }

    [SugarColumn(Length = 64)]
    public string RecorderSerial { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? UserNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? DeptCode { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? StorageLocation { get; set; }

    public DateTime ReceivedAt { get; set; }

    /// <summary>来源采集站归属部门（数据权限过滤）。</summary>
    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }
}
