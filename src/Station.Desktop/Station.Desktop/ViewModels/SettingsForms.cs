using CommunityToolkit.Mvvm.ComponentModel;

namespace Station.Desktop.ViewModels;

/// <summary>基本设置表单（本机编号只读）。</summary>
public partial class BasicSettingsForm : ObservableObject
{
    [ObservableProperty]
    private string _stationNo = string.Empty;

    [ObservableProperty]
    private string _installLocation = string.Empty;

    [ObservableProperty]
    private string _runMode = "standalone";

    [ObservableProperty]
    private string _platformBaseUrl = string.Empty;

    [ObservableProperty]
    private string _platformStationCode = string.Empty;
}

/// <summary>存储策略表单（全局参数；多目标列表在 StorageTargetForm 中）。</summary>
public partial class StorageSettingsForm : ObservableObject
{
    /// <summary>上传目录模板。</summary>
    [ObservableProperty] private string _directoryTemplate = string.Empty;

    /// <summary>熔断阈值（全局默认）。</summary>
    [ObservableProperty] private int _circuitBreakerThreshold = 5;

    /// <summary>熔断冷却秒数（全局默认）。</summary>
    [ObservableProperty] private int _circuitBreakerCooldownSeconds = 60;

    /// <summary>视频缓存保留天数。</summary>
    [ObservableProperty] private int _videoRetentionDays = 90;

    /// <summary>日志保留天数。</summary>
    [ObservableProperty] private int _logRetentionDays = 180;

    /// <summary>清理执行时间（HH:mm）。</summary>
    [ObservableProperty] private string _cleanupTime = "04:00";

    /// <summary>远端内容模式："Plaintext" / "Ciphertext"。</summary>
    [ObservableProperty] private string _remoteContentMode = "Plaintext";

    /// <summary>是否对文件元数据签名。</summary>
    [ObservableProperty] private bool _enableFileSignature = true;

    /// <summary>远端校验模式："None" / "SizeAndSample" / "Full"。</summary>
    [ObservableProperty] private string _remoteVerifyMode = "None";
}

/// <summary>单个存储目标表单（对应 StorageTargetConfig）。</summary>
public partial class StorageTargetForm : ObservableObject
{
    /// <summary>目标类型："Local" / "Ftp" / "Sftp"。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsFtp))]
    [NotifyPropertyChangedFor(nameof(IsSftp))]
    private string _kind = "Local";

    /// <summary>唯一名称（日志/熔断/结果聚合用）。</summary>
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private bool _enabled = true;

    /// <summary>是否主目标（BestEffort 模式下使用）。</summary>
    [ObservableProperty] private bool _isPrimary;

    // ---- Local ----
    [ObservableProperty] private string _localRoot = string.Empty;

    // ---- FTP ----
    [ObservableProperty] private string _ftpHost = string.Empty;
    [ObservableProperty] private int _ftpPort = 21;
    [ObservableProperty] private string _ftpUser = string.Empty;

    /// <summary>明文密码（UI 显示；保存时后端加密）。</summary>
    [ObservableProperty] private string _ftpPassword = string.Empty;

    // ---- SFTP ----
    [ObservableProperty] private string _sftpHost = string.Empty;
    [ObservableProperty] private int _sftpPort = 22;
    [ObservableProperty] private string _sftpUser = string.Empty;
    [ObservableProperty] private string _sftpPassword = string.Empty;
    [ObservableProperty] private string _sftpRoot = string.Empty;

    // ---- 覆盖项 ----
    [ObservableProperty] private int? _retryCount;
    [ObservableProperty] private int? _circuitBreakerThreshold;
    [ObservableProperty] private int? _circuitBreakerCooldownSeconds;

    // ---- 计算属性（用于 UI 显隐） ----
    public bool IsLocal => string.Equals(Kind, "Local", StringComparison.OrdinalIgnoreCase);
    public bool IsFtp => string.Equals(Kind, "Ftp", StringComparison.OrdinalIgnoreCase);
    public bool IsSftp => string.Equals(Kind, "Sftp", StringComparison.OrdinalIgnoreCase);
}


/// <summary>采集策略表单。</summary>
public partial class CollectSettingsForm : ObservableObject
{
    [ObservableProperty] private bool _autoCollectOnConnect = true;
    [ObservableProperty] private bool _eraseAfterComplete;
    [ObservableProperty] private bool _skipCollected = true;
    [ObservableProperty] private bool _collectAncillaryFiles = true;
}

/// <summary>工作台显示表单（卡片布局 + 紧急优先上限）。</summary>
public partial class WorkbenchSettingsForm : ObservableObject
{
    [ObservableProperty] private int _rows = 6;
    [ObservableProperty] private int _columns = 5;
    [ObservableProperty] private int _cardWidth = 240;
    [ObservableProperty] private int _cardHeight = 200;
    [ObservableProperty] private int _maxEmergencyTasks = 3;
}

/// <summary>网络安全表单。</summary>
public partial class NetworkSettingsForm : ObservableObject
{
    [ObservableProperty] private int _webPort = 5000;
    [ObservableProperty] private int _httpsPort = 5443;
    [ObservableProperty] private bool _enableLan;
    [ObservableProperty] private bool _enableHttps;
    [ObservableProperty] private bool _allowHttp;
    [ObservableProperty] private string _certificateStatus = "未安装";
    [ObservableProperty] private int _maxFailedAttempts = 5;
    [ObservableProperty] private int _lockoutMinutes = 15;
}

/// <summary>安全与加密表单（策略下拉选择）。</summary>
public partial class CryptoForm : ObservableObject
{
    // ---- 登录密码 ----
    [ObservableProperty] private string _passwordAlgorithm = "PBKDF2-HMAC-SM3";
    [ObservableProperty] private bool _passwordAllowLegacy = true;

    // ---- 授权文件 ----
    [ObservableProperty] private string _licenseEncrypt = "SM4-GCM";
    [ObservableProperty] private string _licenseSign = "SM2-SM3";

    // ---- 文件签名（摘要 + 签名） ----
    [ObservableProperty] private string _fileSigDigest = "SM3";
    [ObservableProperty] private string _fileSigSign = "SM2-SM3";

    // ---- 敏感字段加密 ----
    [ObservableProperty] private string _secretFieldAlgorithm = "SM4-GCM";

    // ---- 文件内容加密（本地缓存 + 端到端） ----
    [ObservableProperty] private string _fileEncryptionAlgorithm = "SM4-GCM";
}
