using Station.Contracts;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Domain.UploadedFiles;

/// <summary>已上传文件台账（流水表，POCO）。</summary>
public sealed class UploadedFile
{
    public long Id { get; set; }
    public long LocalFileId { get; set; }
    public string FileNo { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public FileKind Kind { get; set; }
    public string ContentDigest { get; set; } = string.Empty;
    public string DigestAlgorithm { get; set; } = string.Empty;
    public string? Signature { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public string RemoteContentMode { get; set; } = "Plaintext";
    public DateTime CollectedAt { get; set; }
    public DateTime? OriginalTime { get; set; }
    public long? UserId { get; set; }
    public long? DeptId { get; set; }
    public TaskStatus Status { get; set; }
}
