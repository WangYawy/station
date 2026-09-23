using Station.Application.Storage;

namespace Station.Application.Storage;

/// <summary>
/// 存储配置，对应配置节 <c>Station:Storage</c>。
/// </summary>
public sealed class StorageOptions //: IStorageConfiguration
{
    public const string SectionName = "Station:Storage";

    // ============ 业务配置 ============
    public string DirectoryTemplate { get; set; } =
        "{StationNo}/{Date:yyyy-MM-dd}/{RecorderName}/{UserId}/{DeptId}/{FileType}";
    public string StationNo { get; set; } = "ST0001";
    public int ChunkBytes { get; set; } = 1024 * 1024;

    // ============ 上传策略 ============
    public int RetryCount { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 30;
    public double RetryJitterFactor { get; set; } = 0.3;
    public int MaxConcurrentUploads { get; set; } = 4;

    // ============ 校验策略 ============
    public RemoteVerifyMode RemoteVerifyMode { get; set; } = RemoteVerifyMode.None;

    /// <summary>远端内容模式：Plaintext（默认，上传明文）/ Ciphertext（端到端加密）。</summary>
    public RemoteContentMode RemoteContentMode { get; set; } = RemoteContentMode.Plaintext;

    /// <summary>是否对文件元数据签名（默认开启）。</summary>
    public bool EnableFileSignature { get; set; } = true;

    // ============ 多 target 结果策略 ============
    public MultiTargetMode MultiTargetMode { get; set; } = MultiTargetMode.AnySuccess;

    // ============ 熔断全局默认 ============
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerCooldownSeconds { get; set; } = 60;

    // ============ 连接池 ============
    public int FtpKeepAliveSeconds { get; set; } = 30;
    public int SftpPoolSize { get; set; } = 4;
    public int SftpKeepAliveSeconds { get; set; } = 30;

    // ============ 取消清理 ============
    public bool CleanupOnCancel { get; set; } = true;
    public bool CleanupOnFailure { get; set; } = false;

    // ============ 多目标列表 ============
    public List<StorageTargetConfig> Targets { get; set; } = new();
}

/// <summary>单个存储目标配置。</summary>
public sealed class StorageTargetConfig
{
    public StorageTargetKind Kind { get; set; }
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsPrimary { get; set; }

    // ---- 本地 ----
    public string? LocalRoot { get; set; }

    // ---- FTP ----
    public string? FtpHost { get; set; }
    public int FtpPort { get; set; } = 21;
    public string? FtpUser { get; set; }
    public string? FtpPassword { get; set; }

    // ---- SFTP ----
    public string? SftpHost { get; set; }
    public int SftpPort { get; set; } = 22;
    public string? SftpUser { get; set; }
    public string? SftpPassword { get; set; }
    public string? SftpRoot { get; set; }

    // ---- 单目标覆盖 ----
    public int? CircuitBreakerThreshold { get; set; }
    public int? CircuitBreakerCooldownSeconds { get; set; }
    public int? RetryCount { get; set; }
    public int? RetryIntervalSeconds { get; set; }
    public RemoteVerifyMode? RemoteVerifyMode { get; set; }
    public RemoteContentMode? RemoteContentMode { get; set; }   // ★ 新增
    public int? FtpKeepAliveSeconds { get; set; }
    public int? SftpKeepAliveSeconds { get; set; }
    public int? SftpPoolSize { get; set; }
}


/// <summary>远端内容模式。</summary>
public enum RemoteContentMode
{
    /// <summary>上传明文（默认，兼容现状）。</summary>
    Plaintext = 0,

    /// <summary>上传密文（端到端加密）。</summary>
    Ciphertext = 1
}
