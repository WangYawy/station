using SqlSugar;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

[SugarTable("station_collect_file")]
public sealed class CollectFilePo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long TaskId { get; set; }

    [SugarColumn(Length = 128)]
    public string TaskNo { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string FileNo { get; set; } = string.Empty;

    [SugarColumn(Length = 255)]
    public string FileName { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? Extension { get; set; }

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? RelativePath { get; set; }

    public long Size { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Fingerprint { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? ContentDigest { get; set; }

    [SugarColumn(Length = 32, IsNullable = true)]
    public string? DigestAlgorithm { get; set; }

    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Signature { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SignatureAlgorithm { get; set; }

    public bool LocalEncrypted { get; set; }
    public long EncryptedSize { get; set; }

    public CollectFileStatus Status { get; set; }
    public double Progress { get; set; }
    public double SpeedBytesPerSecond { get; set; }

    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ErrorMessage { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CollectedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? OriginalModifiedAt { get; set; }

    public UploadStatus SyncStatus { get; set; }
    public double UploadProgress { get; set; }
    public int UploadRetryCount { get; set; }

    [SugarColumn(Length = 512, IsNullable = true)]
    public string? RemotePath { get; set; }

    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? UploadError { get; set; }
}
