using Station.Application.Storage;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储配置，对应配置节 <c>Station:Storage</c>。
/// 支持 本地磁盘 / FTP / SFTP；上传目录模板变量：
/// {StationNo} {Date} {RecorderName} {UserId} {DeptId} {FileType}。
/// </summary>
public sealed class StorageOptions : IStorageConfiguration
{
    public const string SectionName = "Station:Storage";

    // 业务配置（实现接口）
    public string DirectoryTemplate { get; set; } = "{StationNo}/{Date:yyyy-MM-dd}/{RecorderName}/{UserId}/{DeptId}/{FileType}";
    public string StationNo { get; set; } = "ST0001";
    public int RetryCount { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 10;
    public bool VerifyRemoteSm3 { get; set; } = true;

    // 技术配置（熔断、分块大小）
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerCooldownSeconds { get; set; } = 60;
    public int ChunkBytes { get; set; } = 1024 * 1024;

    // 多存储目标配置（支持同时上传到多个位置）
    public List<StorageTargetConfig> Targets { get; set; } = new();
}
public class StorageTargetConfig
{
    public StorageTargetKind Kind { get; set; }
    public string? Name { get; set; } // 可选标识

    // 本地存储专用
    public string? LocalRoot { get; set; }

    // FTP 专用
    public string? FtpHost { get; set; }
    public int FtpPort { get; set; } = 21;
    public string? FtpUser { get; set; }
    public string? FtpPassword { get; set; }

    // SFTP 专用
    public string? SftpHost { get; set; }
    public int SftpPort { get; set; } = 22;
    public string? SftpUser { get; set; }
    public string? SftpPassword { get; set; }
    public string? SftpRoot { get; set; }

    // 熔断配置（可选，若未设置则使用全局默认值）
    public int? CircuitBreakerThreshold { get; set; }
    public int? CircuitBreakerCooldownSeconds { get; set; }
}
public enum StorageTargetKind
{
    Local = 0,
    Ftp = 1,
    Sftp = 2
}
