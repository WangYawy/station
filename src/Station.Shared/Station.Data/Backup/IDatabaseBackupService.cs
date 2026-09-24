namespace Station.Data.Backup;

public sealed record BackupFileInfo(string FileName, string FullPath, long Size, DateTime CreatedAt);

public interface IDatabaseBackupService
{
    Task<string> CreateBackupAsync();

    IReadOnlyList<BackupFileInfo> ListBackups();

    Task<int> RestoreAsync(string backupPath);
}
