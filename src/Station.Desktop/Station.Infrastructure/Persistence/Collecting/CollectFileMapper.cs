using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

internal static class CollectFileMapper
{
    public static CollectFile ToDomain(CollectFilePo po)
        => CollectFile.Restore(
            po.Id, po.TaskId, po.TaskNo, po.FileNo,
            po.FileName, po.Extension, po.RelativePath, po.Size,
            po.Fingerprint, po.ContentDigest, po.DigestAlgorithm,
            po.Signature, po.SignatureAlgorithm,
            po.LocalEncrypted, po.EncryptedSize,
            po.Status, po.Progress, po.SpeedBytesPerSecond,
            po.ErrorMessage, po.CollectedAt, po.OriginalModifiedAt,
            po.SyncStatus, po.UploadProgress, po.UploadRetryCount,
            po.RemotePath, po.UploadError);

    public static CollectFilePo ToPo(CollectFile f)
        => new()
        {
            Id = f.Id,
            TaskId = f.TaskId,
            TaskNo = f.TaskNo,
            FileNo = f.FileNo,
            FileName = f.FileName,
            Extension = f.Extension,
            RelativePath = f.RelativePath,
            Size = f.Size,
            Fingerprint = f.Fingerprint,
            ContentDigest = f.ContentDigest,
            DigestAlgorithm = f.DigestAlgorithm,
            Signature = f.Signature,
            SignatureAlgorithm = f.SignatureAlgorithm,
            LocalEncrypted = f.LocalEncrypted,
            EncryptedSize = f.EncryptedSize,
            Status = f.Status,
            Progress = f.Progress,
            SpeedBytesPerSecond = f.SpeedBytesPerSecond,
            ErrorMessage = f.ErrorMessage,
            CollectedAt = f.CollectedAt,
            OriginalModifiedAt = f.OriginalModifiedAt,
            SyncStatus = f.SyncStatus,
            UploadProgress = f.UploadProgress,
            UploadRetryCount = f.UploadRetryCount,
            RemotePath = f.RemotePath,
            UploadError = f.UploadError
        };
}
