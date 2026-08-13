namespace Station.Contracts.Reporting;

/// <summary>平台文件统一列表查询结果。</summary>
public sealed record FileMetadataView(
    long StationId,
    long LocalFileId,
    string FileNo,
    string FileName,
    long Size,
    FileKind Kind,
    string Sm3,
    DateTime CollectedAt,
    string RecorderSerial,
    string? UserNo,
    string? DeptCode,
    string? StorageLocation);
