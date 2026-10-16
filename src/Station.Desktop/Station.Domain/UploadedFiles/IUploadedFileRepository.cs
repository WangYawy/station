namespace Station.Domain.UploadedFiles;

public interface IUploadedFileRepository
{
    Task<bool> ExistsByLocalFileIdAsync(long localFileId, CancellationToken ct = default);
    Task AddAsync(UploadedFile file, CancellationToken ct = default);
}
