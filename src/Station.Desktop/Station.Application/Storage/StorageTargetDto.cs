namespace Station.Application.Storage;

/// <summary>
/// 存储目标传输 DTO。
/// 
/// 【密码字段语义】
///   - FtpPasswordPlain / SftpPasswordPlain：明文，仅用于 UI 交互，不落库；
///   - 服务层收到后加密存储，读回时解密填入明文字段；
///   - null 或空串表示"不修改"（保存时保留原密文）。
/// 
/// 【不落库】本 DTO 只用于 Application↔UI 传输，DB 持久化用 Infrastructure 内部模型。
/// </summary>
public sealed class StorageTargetDto
{
    /// <summary>目标类型："Local" / "Ftp" / "Sftp"。</summary>
    public string Kind { get; set; } = "Local";

    /// <summary>唯一名称（日志/熔断/结果聚合用）。</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>是否主目标（BestEffort 模式下使用）。</summary>
    public bool IsPrimary { get; set; }

    // ---- Local ----
    public string? LocalRoot { get; set; }

    // ---- FTP ----
    public string? FtpHost { get; set; }
    public int FtpPort { get; set; } = 21;
    public string? FtpUser { get; set; }

    /// <summary>明文密码（UI 输入/输出；不落库）。</summary>
    public string? FtpPasswordPlain { get; set; }

    // ---- SFTP ----
    public string? SftpHost { get; set; }
    public int SftpPort { get; set; } = 22;
    public string? SftpUser { get; set; }

    /// <summary>明文密码（UI 输入/输出；不落库）。</summary>
    public string? SftpPasswordPlain { get; set; }

    public string? SftpRoot { get; set; }

    // ---- 覆盖项 ----
    public int? RetryCount { get; set; }
    public int? CircuitBreakerThreshold { get; set; }
    public int? CircuitBreakerCooldownSeconds { get; set; }
}
