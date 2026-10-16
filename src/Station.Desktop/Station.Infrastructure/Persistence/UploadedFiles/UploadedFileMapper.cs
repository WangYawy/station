using Station.Domain.UploadedFiles;

namespace Station.Infrastructure.Persistence.UploadedFiles;

internal static class UploadedFileMapper
{
    public static UploadedFile ToDomain(UploadedFilePo po) => new()
    {
        Id = po.Id,
        LocalFileId = po.LocalFileId,
        FileNo = po.FileNo,
        FileName = po.FileName,
        Size = po.Size,
        Kind = po.Kind,
        ContentDigest = po.ContentDigest,
        DigestAlgorithm = po.DigestAlgorithm,
        Signature = po.Signature,
        SignatureAlgorithm = po.SignatureAlgorithm,
        RemoteContentMode = po.RemoteContentMode,
        CollectedAt = po.CollectedAt,
        OriginalTime = po.OriginalTime,
        UserId = po.UserId,
        DeptId = po.DeptId,
        Status = po.Status
    };

    public static UploadedFilePo ToPo(UploadedFile f) => new()
    {
        Id = f.Id,
        LocalFileId = f.LocalFileId,
        FileNo = f.FileNo,
        FileName = f.FileName,
        Size = f.Size,
        Kind = f.Kind,
        ContentDigest = f.ContentDigest,
        DigestAlgorithm = f.DigestAlgorithm,
        Signature = f.Signature,
        SignatureAlgorithm = f.SignatureAlgorithm,
        RemoteContentMode = f.RemoteContentMode,
        CollectedAt = f.CollectedAt,
        OriginalTime = f.OriginalTime,
        UserId = f.UserId,
        DeptId = f.DeptId,
        Status = f.Status
    };
}
