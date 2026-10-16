using SqlSugar;
using Station.Contracts;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Infrastructure.Persistence.UploadedFiles;

[SugarTable("station_uploaded_file")]
[SugarIndex("uk_uploadedfile_fileno", nameof(FileNo), OrderByType.Asc, true)]
public sealed class UploadedFilePo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public long LocalFileId { get; set; }
    [SugarColumn(Length = 64)] public string FileNo { get; set; } = "";
    [SugarColumn(Length = 255)] public string FileName { get; set; } = "";
    public long Size { get; set; }
    public FileKind Kind { get; set; }
    [SugarColumn(Length = 128)] public string ContentDigest { get; set; } = "";
    [SugarColumn(Length = 32)] public string DigestAlgorithm { get; set; } = "";
    [SugarColumn(ColumnDataType = "text", IsNullable = true)] public string? Signature { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? SignatureAlgorithm { get; set; }
    [SugarColumn(Length = 16)] public string RemoteContentMode { get; set; } = "Plaintext";
    public DateTime CollectedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? OriginalTime { get; set; }
    [SugarColumn(IsNullable = true)] public long? UserId { get; set; }
    [SugarColumn(IsNullable = true)] public long? DeptId { get; set; }
    public TaskStatus Status { get; set; }
}
