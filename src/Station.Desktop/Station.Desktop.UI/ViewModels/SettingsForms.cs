using CommunityToolkit.Mvvm.ComponentModel;

namespace Station.Desktop.UI.ViewModels;

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

/// <summary>存储策略表单。</summary>
public partial class StorageSettingsForm : ObservableObject
{
    [ObservableProperty]
    private string _target = "Local";

    [ObservableProperty]
    private string _localRoot = string.Empty;

    [ObservableProperty]
    private string _directoryTemplate = string.Empty;

    [ObservableProperty]
    private string _ftpHost = string.Empty;

    [ObservableProperty]
    private int _ftpPort = 21;

    [ObservableProperty]
    private string _ftpUser = string.Empty;

    [ObservableProperty]
    private string _ftpPassword = string.Empty;

    [ObservableProperty]
    private string _sftpHost = string.Empty;

    [ObservableProperty]
    private int _sftpPort = 22;

    [ObservableProperty]
    private string _sftpUser = string.Empty;

    [ObservableProperty]
    private string _sftpPassword = string.Empty;

    [ObservableProperty]
    private string _sftpRoot = string.Empty;

    [ObservableProperty]
    private int _circuitBreakerThreshold = 5;

    [ObservableProperty]
    private int _circuitBreakerCooldownSeconds = 60;

    [ObservableProperty]
    private int _videoRetentionDays = 90;

    [ObservableProperty]
    private int _logRetentionDays = 180;

    [ObservableProperty]
    private string _cleanupTime = "04:00";
}

/// <summary>采集策略表单。</summary>
public partial class CollectSettingsForm : ObservableObject
{
    [ObservableProperty]
    private bool _autoCollectOnConnect = true;

    [ObservableProperty]
    private bool _eraseAfterComplete;

    [ObservableProperty]
    private bool _skipCollected = true;

    [ObservableProperty]
    private bool _collectAncillaryFiles = true;
}

/// <summary>网络安全表单。</summary>
public partial class NetworkSettingsForm : ObservableObject
{
    [ObservableProperty]
    private int _webPort = 5000;

    [ObservableProperty]
    private int _httpsPort = 5443;

    [ObservableProperty]
    private bool _enableLan;

    [ObservableProperty]
    private bool _enableHttps;

    [ObservableProperty]
    private bool _allowHttp;

    [ObservableProperty]
    private string _certificateStatus = "未安装";

    [ObservableProperty]
    private int _maxFailedAttempts = 5;

    [ObservableProperty]
    private int _lockoutMinutes = 15;
}
