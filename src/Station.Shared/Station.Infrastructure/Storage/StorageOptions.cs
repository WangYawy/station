namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储配置，对应配置节 <c>Station:Storage</c>。
/// 支持 本地磁盘 / FTP / SFTP；上传目录模板变量：
/// {StationNo} {Date} {RecorderName} {UserId} {DeptId} {FileType}。
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Station:Storage";

    public StorageTargetKind Target { get; set; } = StorageTargetKind.Local;

    public string LocalRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Station", "storage");

    public string FtpHost { get; set; } = "localhost";

    public int FtpPort { get; set; } = 21;

    public string? FtpUser { get; set; }

    public string? FtpPassword { get; set; }

    public string SftpHost { get; set; } = "localhost";

    public int SftpPort { get; set; } = 22;

    public string? SftpUser { get; set; }

    public string? SftpPassword { get; set; }

    /// <summary>上传目录模板，默认 采集站/日期/记录仪/用户/部门/类型。</summary>
    public string DirectoryTemplate { get; set; } =
        "{StationNo}/{Date:yyyy-MM-dd}/{RecorderName}/{UserId}/{DeptId}/{FileType}";

    /// <summary>本机采集站编号。</summary>
    public string StationNo { get; set; } = "ST0001";

    /// <summary>单文件重试次数。</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>单文件重试间隔（秒）。</summary>
    public int RetryIntervalSeconds { get; set; } = 10;

    /// <summary>熔断阈值：连续失败次数。</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>熔断冷却时间（秒）。</summary>
    public int CircuitBreakerCooldownSeconds { get; set; } = 60;

    public int ChunkBytes { get; set; } = 1024 * 1024;
}

public enum StorageTargetKind
{
    Local = 0,
    Ftp = 1,
    Sftp = 2
}
