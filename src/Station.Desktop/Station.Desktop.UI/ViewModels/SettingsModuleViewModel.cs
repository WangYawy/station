using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authorization;
using Station.Application.Licensing;
using Station.Application.Settings;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Desktop.Application.Settings;
using Station.Desktop.WebHost.Settings;
using Station.Infrastructure.Persistence;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 桌面端设置模块：基本/存储/采集/网络/授权/自检（对应单机版 Web 设置页）。
/// 查看需登录 + setting:view（ShellWindow 校验），修改需 setting:manage。
/// </summary>
public partial class SettingsModuleViewModel : ObservableObject, IDisposable
{
    private readonly ISystemSettingsService _settings;
    private readonly INetworkSettingsService _network;
    private readonly ISystemSelfCheckService _selfCheck;
    private readonly ILicenseService _license;

    public BasicSettingsForm Basic { get; } = new();

    public StorageSettingsForm Storage { get; } = new();

    public CollectSettingsForm Collect { get; } = new();

    public WorkbenchSettingsForm Workbench { get; } = new();

    public NetworkSettingsForm Network { get; } = new();

    public ObservableCollection<SelfCheckItemDto> SelfCheckItems { get; } = [];

    [ObservableProperty]
    private string _licenseStatus = "加载中…";

    [ObservableProperty]
    private string _licenseText = string.Empty;

    [ObservableProperty]
    private string _selfCheckTime = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _canManage;

    public bool CanEdit => CanManage && !IsReadOnly;

    public int RunModeIndex
    {
        get => Basic.RunMode == "platform" ? 1 : 0;
        set
        {
            Basic.RunMode = value == 1 ? "platform" : "standalone";
            OnPropertyChanged();
        }
    }

    public int StorageTargetIndex
    {
        get => Storage.Target switch { "Ftp" => 1, "Sftp" => 2, _ => 0 };
        set
        {
            Storage.Target = value switch { 1 => "Ftp", 2 => "Sftp", _ => "Local" };
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowLocalTarget));
            OnPropertyChanged(nameof(ShowFtpTarget));
            OnPropertyChanged(nameof(ShowSftpTarget));
        }
    }

    public bool ShowLocalTarget => Storage.Target == "Local";

    public bool ShowFtpTarget => Storage.Target == "Ftp";

    public bool ShowSftpTarget => Storage.Target == "Sftp";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isSelfChecking;

    [ObservableProperty]
    private bool _isActivating;

    public SettingsModuleViewModel(
        ISystemSettingsService settings,
        INetworkSettingsService network,
        ISystemSelfCheckService selfCheck,
        ILicenseService license,
        ISessionManager sessions,
        IOperationAccessService operationAccess)
    {
        _settings = settings;
        _network = network;
        _selfCheck = selfCheck;
        _license = license;

        var session = sessions.Current;
        CanManage = operationAccess.HasPermission(session, "settings") &&
                    (session?.Roles.Contains(AuthRoleCodes.Admin) == true ||
                     session?.Permissions.Contains(Domain.Authorization.PermissionCodes.SettingManage) == true);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var core = await _settings.GetCoreAsync();
            var network = _network.Get();

            Basic.StationNo = core.Basic.StationNo;
            Basic.InstallLocation = core.Basic.InstallLocation;
            Basic.RunMode = core.Basic.RunMode;
            Basic.PlatformBaseUrl = core.Basic.PlatformBaseUrl;
            Basic.PlatformStationCode = core.Basic.PlatformStationCode;

            Storage.Target = core.Storage.Target;
            Storage.LocalRoot = core.Storage.LocalRoot;
            Storage.DirectoryTemplate = core.Storage.DirectoryTemplate;
            Storage.FtpHost = core.Storage.FtpHost;
            Storage.FtpPort = core.Storage.FtpPort;
            Storage.FtpUser = core.Storage.FtpUser;
            Storage.SftpHost = core.Storage.SftpHost;
            Storage.SftpPort = core.Storage.SftpPort;
            Storage.SftpUser = core.Storage.SftpUser;
            Storage.SftpRoot = core.Storage.SftpRoot;
            Storage.CircuitBreakerThreshold = core.Storage.CircuitBreakerThreshold;
            Storage.CircuitBreakerCooldownSeconds = core.Storage.CircuitBreakerCooldownSeconds;
            Storage.VideoRetentionDays = core.Storage.VideoRetentionDays;
            Storage.LogRetentionDays = core.Storage.LogRetentionDays;
            Storage.CleanupTime = core.Storage.CleanupTime;

            Collect.AutoCollectOnConnect = core.Collect.AutoCollectOnConnect;
            Collect.EraseAfterComplete = core.Collect.EraseAfterComplete;
            Collect.SkipCollected = core.Collect.SkipCollected;
            Collect.CollectAncillaryFiles = core.Collect.CollectAncillaryFiles;

            Workbench.Rows = core.Workbench.Rows;
            Workbench.Columns = core.Workbench.Columns;
            Workbench.CardWidth = core.Workbench.CardWidth;
            Workbench.CardHeight = core.Workbench.CardHeight;
            Workbench.MaxEmergencyTasks = core.Workbench.MaxEmergencyTasks;

            Network.WebPort = network.WebPort;
            Network.HttpsPort = network.HttpsPort;
            Network.EnableLan = network.EnableLan;
            Network.EnableHttps = network.EnableHttps;
            Network.AllowHttp = network.AllowHttp;
            Network.CertificateStatus = network.CertificateStatus;
            Network.MaxFailedAttempts = network.MaxFailedAttempts;
            Network.LockoutMinutes = network.LockoutMinutes;

            LicenseStatus = core.License.Message;
            IsReadOnly = core.ReadOnly;
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载设置失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveBasicAsync()
    {
        await SaveCoreAsync("basic", new Dictionary<string, string>
        {
            ["installLocation"] = Basic.InstallLocation,
            ["runMode"] = Basic.RunMode,
            ["platformBaseUrl"] = Basic.PlatformBaseUrl,
            ["platformStationCode"] = Basic.PlatformStationCode
        });
    }

    [RelayCommand]
    private async Task SaveStorageAsync()
    {
        var values = new Dictionary<string, string>
        {
            ["target"] = Storage.Target,
            ["localRoot"] = Storage.LocalRoot,
            ["directoryTemplate"] = Storage.DirectoryTemplate,
            ["ftpHost"] = Storage.FtpHost,
            ["ftpPort"] = Storage.FtpPort.ToString(),
            ["ftpUser"] = Storage.FtpUser,
            ["sftpHost"] = Storage.SftpHost,
            ["sftpPort"] = Storage.SftpPort.ToString(),
            ["sftpUser"] = Storage.SftpUser,
            ["sftpRoot"] = Storage.SftpRoot,
            ["circuitBreakerThreshold"] = Storage.CircuitBreakerThreshold.ToString(),
            ["circuitBreakerCooldownSeconds"] = Storage.CircuitBreakerCooldownSeconds.ToString(),
            ["videoRetentionDays"] = Storage.VideoRetentionDays.ToString(),
            ["logRetentionDays"] = Storage.LogRetentionDays.ToString(),
            ["cleanupTime"] = Storage.CleanupTime
        };
        if (!string.IsNullOrWhiteSpace(Storage.FtpPassword))
        {
            values["ftpPassword"] = Storage.FtpPassword;
            Storage.FtpPassword = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(Storage.SftpPassword))
        {
            values["sftpPassword"] = Storage.SftpPassword;
            Storage.SftpPassword = string.Empty;
        }

        await SaveCoreAsync("storage", values);
    }

    [RelayCommand]
    private async Task SaveCollectAsync()
    {
        await SaveCoreAsync("collect", new Dictionary<string, string>
        {
            ["autoCollectOnConnect"] = Collect.AutoCollectOnConnect.ToString(),
            ["eraseAfterComplete"] = Collect.EraseAfterComplete.ToString(),
            ["skipCollected"] = Collect.SkipCollected.ToString(),
            ["collectAncillaryFiles"] = Collect.CollectAncillaryFiles.ToString()
        });
    }

    [RelayCommand]
    private async Task SaveWorkbenchAsync()
    {
        await SaveCoreAsync("workbench", new Dictionary<string, string>
        {
            ["rows"] = Workbench.Rows.ToString(),
            ["columns"] = Workbench.Columns.ToString(),
            ["cardWidth"] = Workbench.CardWidth.ToString(),
            ["cardHeight"] = Workbench.CardHeight.ToString(),
            ["maxEmergencyTasks"] = Workbench.MaxEmergencyTasks.ToString()
        });
    }

    private async Task SaveCoreAsync(string group, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            var hints = await _settings.UpdateAsync(group, values, "desktop");
            StatusMessage = hints.Count > 0 ? string.Join("；", hints) : "保存成功";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveNetworkAsync()
    {
        try
        {
            var hints = _network.Update(new Dictionary<string, string>
            {
                ["webPort"] = Network.WebPort.ToString(),
                ["httpsPort"] = Network.HttpsPort.ToString(),
                ["enableLan"] = Network.EnableLan.ToString(),
                ["enableHttps"] = Network.EnableHttps.ToString(),
                ["allowHttp"] = Network.AllowHttp.ToString(),
                ["maxFailedAttempts"] = Network.MaxFailedAttempts.ToString(),
                ["lockoutMinutes"] = Network.LockoutMinutes.ToString()
            });
            StatusMessage = hints.Count > 0 ? string.Join("；", hints) : "保存成功";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseText))
        {
            StatusMessage = "请粘贴授权文件内容";
            return;
        }

        IsActivating = true;
        try
        {
            var (ok, message) = await _license.ActivateAsync(LicenseText);
            StatusMessage = message;
            if (ok)
            {
                LicenseText = string.Empty;
                var core = await _settings.GetCoreAsync();
                LicenseStatus = core.License.Message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"激活失败：{ex.Message}";
        }
        finally
        {
            IsActivating = false;
        }
    }

    [RelayCommand]
    private async Task RunSelfCheckAsync()
    {
        IsSelfChecking = true;
        try
        {
            var items = await _selfCheck.RunAsync();
            SelfCheckItems.Clear();
            foreach (var item in items)
            {
                SelfCheckItems.Add(item);
            }

            SelfCheckTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            StatusMessage = $"自检失败：{ex.Message}";
        }
        finally
        {
            IsSelfChecking = false;
        }
    }

    public void Dispose()
    {
    }
}
