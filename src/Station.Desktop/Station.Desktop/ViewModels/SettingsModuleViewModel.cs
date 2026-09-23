using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Application.Licensing;
using Station.Application.OperationAccess;
using Station.Application.Security.Abstractions;    // ← 命名空间：接口
using Station.Application.Security.Models;         // ← 命名空间：模型
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.Storage;
using Station.Domain.Security;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 桌面端设置模块：基本/存储/采集/网络/授权/自检 + 安全与加密。
/// 查看需登录 + setting:view，修改需 setting:manage。
/// </summary>
public partial class SettingsModuleViewModel : ObservableObject, IDisposable
{
    private readonly ISystemSettingsService _settings;
    private readonly ISystemSelfCheckService _selfCheck;
    private readonly ILicenseService _license;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly IKeyRotationService _keyRotation;
    private readonly IAuditLogService _auditService;

    // ---- 表单 ----
    public BasicSettingsForm Basic { get; } = new();
    public StorageSettingsForm Storage { get; } = new();
    public CollectSettingsForm Collect { get; } = new();
    public WorkbenchSettingsForm Workbench { get; } = new();
    public NetworkSettingsForm Network { get; } = new();
    public CryptoForm Crypto { get; } = new();

    /// <summary>存储目标列表（多目标管理）。</summary>
    public ObservableCollection<StorageTargetForm> StorageTargets { get; } = [];

    public ObservableCollection<SelfCheckItemDto> SelfCheckItems { get; } = [];
    public ObservableCollection<CryptoAuditDto> CryptoAudits { get; } = [];

    [ObservableProperty] private string _licenseStatus = "加载中…";
    [ObservableProperty] private string _licenseText = string.Empty;
    [ObservableProperty] private string _selfCheckTime = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _masterKeyStatus = "加载中…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _canManage;

    public bool CanEdit => CanManage && !IsReadOnly;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isSelfChecking;
    [ObservableProperty] private bool _isActivating;
    [ObservableProperty] private bool _isMigrating;
    [ObservableProperty] private bool _isRotating;

    /// <summary>运行模式下拉索引（0=单机，1=平台）。</summary>
    public int RunModeIndex
    {
        get => Basic.RunMode == "platform" ? 1 : 0;
        set { Basic.RunMode = value == 1 ? "platform" : "standalone"; OnPropertyChanged(); }
    }

    /// <summary>远端内容模式下拉索引（0=明文，1=密文）。</summary>
    public int RemoteContentModeIndex
    {
        get => Storage.RemoteContentMode == "Ciphertext" ? 1 : 0;
        set
        {
            Storage.RemoteContentMode = value == 1 ? "Ciphertext" : "Plaintext";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCiphertextWarning));
        }
    }
    public bool ShowCiphertextWarning => Storage.RemoteContentMode == "Ciphertext";

    /// <summary>远端校验模式下拉索引（0=None, 1=SizeAndSample, 2=Full）。</summary>
    public int RemoteVerifyModeIndex
    {
        get => Storage.RemoteVerifyMode switch
        {
            "SizeAndSample" => 1,
            "Full" => 2,
            _ => 0
        };
        set
        {
            Storage.RemoteVerifyMode = value switch
            {
                1 => "SizeAndSample",
                2 => "Full",
                _ => "None"
            };
            OnPropertyChanged();
        }
    }

    public SettingsModuleViewModel(
        ISystemSettingsService settings,
        ISystemSelfCheckService selfCheck,
        ILicenseService license,
        ISessionManager sessions,
        IOperationAccessService operationAccess,
        ICryptoPolicyService cryptoPolicy,
        IKeyRotationService keyRotation,
        IAuditLogService auditService)
    {
        _settings = settings;
        _selfCheck = selfCheck;
        _license = license;
        _cryptoPolicy = cryptoPolicy;
        _keyRotation = keyRotation;
        _auditService = auditService;

        var session = sessions.Current;
        CanManage = operationAccess.HasPermission(session, "settings") &&
                    (session?.Roles.Contains(AuthRoleCodes.Admin) == true ||
                     session?.Permissions.Contains(Domain.Authorization.PermissionCodes.SettingManage) == true);

        _ = LoadAsync();
    }

    // =========================================================
    // 加载
    // =========================================================

    private async Task LoadAsync()
    {
        try
        {
            var core = await _settings.GetCoreAsync();

            Basic.StationNo = core.Basic.StationNo;
            Basic.InstallLocation = core.Basic.InstallLocation;
            Basic.RunMode = core.Basic.RunMode;
            Basic.PlatformBaseUrl = core.Basic.PlatformBaseUrl;
            Basic.PlatformStationCode = core.Basic.PlatformStationCode;

            // 存储全局参数
            Storage.DirectoryTemplate = core.Storage.DirectoryTemplate;
            Storage.CircuitBreakerThreshold = core.Storage.CircuitBreakerThreshold;
            Storage.CircuitBreakerCooldownSeconds = core.Storage.CircuitBreakerCooldownSeconds;
            Storage.VideoRetentionDays = core.Storage.VideoRetentionDays;
            Storage.LogRetentionDays = core.Storage.LogRetentionDays;
            Storage.CleanupTime = core.Storage.CleanupTime;
            //Storage.RemoteContentMode = core.Storage.RemoteContentMode ?? "Plaintext";
            //Storage.EnableFileSignature = core.Storage.EnableFileSignature;
            //Storage.RemoteVerifyMode = core.Storage.RemoteVerifyMode ?? "None";

            // 采集/工作台
            Collect.AutoCollectOnConnect = core.Collect.AutoCollectOnConnect;
            Collect.EraseAfterComplete = core.Collect.EraseAfterComplete;
            Collect.SkipCollected = core.Collect.SkipCollected;
            Collect.CollectAncillaryFiles = core.Collect.CollectAncillaryFiles;

            Workbench.Rows = core.Workbench.Rows;
            Workbench.Columns = core.Workbench.Columns;
            Workbench.CardWidth = core.Workbench.CardWidth;
            Workbench.CardHeight = core.Workbench.CardHeight;
            Workbench.MaxEmergencyTasks = core.Workbench.MaxEmergencyTasks;

            LicenseStatus = core.License.Message;
            IsReadOnly = core.ReadOnly;

            // 加载存储目标列表
            await LoadStorageTargetsAsync();

            // 加载加密策略
            await LoadCryptoAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载设置失败：{ex.Message}";
        }
        finally { IsLoading = false; }
    }

    /// <summary>从 ISystemSettingsService 读多目标列表。</summary>
    private async Task LoadStorageTargetsAsync()
    {
        try
        {
            var list = await _settings.GetStorageTargetsAsync();
            StorageTargets.Clear();
            foreach (var dto in list)
            {
                StorageTargets.Add(new StorageTargetForm
                {
                    Kind = dto.Kind.ToString(),
                    Name = dto.Name,
                    Enabled = dto.Enabled,
                    IsPrimary = dto.IsPrimary,
                    LocalRoot = dto.LocalRoot ?? string.Empty,
                    FtpHost = dto.FtpHost ?? string.Empty,
                    FtpPort = dto.FtpPort,
                    FtpUser = dto.FtpUser ?? string.Empty,
                    //  FtpPassword = dto.FtpPasswordPlain ?? string.Empty,
                    SftpHost = dto.SftpHost ?? string.Empty,
                    SftpPort = dto.SftpPort,
                    SftpUser = dto.SftpUser ?? string.Empty,
                    // SftpPassword = dto.SftpPasswordPlain ?? string.Empty,
                    SftpRoot = dto.SftpRoot ?? string.Empty,
                    RetryCount = dto.RetryCount,
                    CircuitBreakerThreshold = dto.CircuitBreakerThreshold
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载存储目标失败：{ex.Message}";
        }
    }

    private async Task LoadCryptoAsync()
    {
        var all = await _cryptoPolicy.GetAllAsync();

        Crypto.PasswordAlgorithm = Find(all, CryptoUsage.Password)?.Algorithm ?? CryptoAlgorithm.Sm3Pbkdf2;
        Crypto.PasswordAllowLegacy = Find(all, CryptoUsage.Password)?.AllowLegacy ?? true;

        Crypto.LicenseEncrypt = Find(all, CryptoUsage.License)?.Algorithm ?? CryptoAlgorithm.Sm4Gcm;
        Crypto.LicenseSign = Find(all, CryptoUsage.License)?.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;

        Crypto.FileSigDigest = Find(all, CryptoUsage.FileSig)?.Algorithm ?? CryptoAlgorithm.Sm3;
        Crypto.FileSigSign = Find(all, CryptoUsage.FileSig)?.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;

        Crypto.SecretFieldAlgorithm = Find(all, CryptoUsage.SecretField)?.Algorithm ?? CryptoAlgorithm.Sm4Gcm;

        // ★ 新增：文件加密
        Crypto.FileEncryptionAlgorithm = Find(all, CryptoUsage.FileEncryption)?.Algorithm ?? CryptoAlgorithm.Sm4Gcm;

        MasterKeyStatus = "密钥文件 · 当前版本待刷新";

        await LoadCryptoAuditsAsync();

        static CryptoPolicySnapshot? Find(IReadOnlyList<CryptoPolicySnapshot> list, string usage)
            => list.FirstOrDefault(x => string.Equals(x.UsageCode, usage, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadCryptoAuditsAsync()
    {
        var logs = await _auditService.GetRecentAsync(
            new[] { "CryptoPolicyChange", "MasterKeyRotate", "PasswordMigrate" }, 20);
        CryptoAudits.Clear();
        foreach (var l in logs)
        {
            CryptoAudits.Add(new CryptoAuditDto
            {
                Time = l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Operator = l.OperatorAccount ?? "-",
                Target = l.Target ?? "-",
                Detail = l.Detail ?? "-",
                Result = l.Result == 1 ? "成功" : "失败"
            });
        }
    }

    // =========================================================
    // 保存 - 基本
    // =========================================================

    [RelayCommand]
    private async Task SaveBasicAsync() => await SaveCoreAsync("basic", new Dictionary<string, string>
    {
        ["installLocation"] = Basic.InstallLocation,
        ["runMode"] = Basic.RunMode,
        ["platformBaseUrl"] = Basic.PlatformBaseUrl,
        ["platformStationCode"] = Basic.PlatformStationCode
    });

    // =========================================================
    // 保存 - 存储（全局参数 + 多目标）
    // =========================================================

    [RelayCommand]
    private async Task SaveStorageAsync()
    {
        var values = new Dictionary<string, string>
        {
            ["directoryTemplate"] = Storage.DirectoryTemplate,
            ["circuitBreakerThreshold"] = Storage.CircuitBreakerThreshold.ToString(),
            ["circuitBreakerCooldownSeconds"] = Storage.CircuitBreakerCooldownSeconds.ToString(),
            ["videoRetentionDays"] = Storage.VideoRetentionDays.ToString(),
            ["logRetentionDays"] = Storage.LogRetentionDays.ToString(),
            ["cleanupTime"] = Storage.CleanupTime,
            ["remoteContentMode"] = Storage.RemoteContentMode,
            ["enableFileSignature"] = Storage.EnableFileSignature.ToString(),
            ["remoteVerifyMode"] = Storage.RemoteVerifyMode
        };

        // 全局参数走原有 UpdateAsync
        await SaveCoreAsync("storage", values);

        // 多目标列表单独保存（走 ISystemSettingsService.UpdateStorageTargetsAsync）
        try
        {
            var dtos = StorageTargets.Select(t => new StorageTargetConfig
            {
                //Kind = t.Kind,
                Name = t.Name,
                Enabled = t.Enabled,
                IsPrimary = t.IsPrimary,
                LocalRoot = t.LocalRoot,
                FtpHost = t.FtpHost,
                FtpPort = t.FtpPort,
                FtpUser = t.FtpUser,
                // FtpPasswordPlain = string.IsNullOrWhiteSpace(t.FtpPassword) ? null : t.FtpPassword,
                SftpHost = t.SftpHost,
                SftpPort = t.SftpPort,
                SftpUser = t.SftpUser,
                //SftpPasswordPlain = string.IsNullOrWhiteSpace(t.SftpPassword) ? null : t.SftpPassword,
                SftpRoot = t.SftpRoot,
                RetryCount = t.RetryCount,
                CircuitBreakerThreshold = t.CircuitBreakerThreshold
            }).ToList();

            await _settings.UpdateStorageTargetsAsync(dtos, "desktop");

            // 保存成功后清空密码框（避免明文停留内存/UI）
            foreach (var t in StorageTargets)
            {
                t.FtpPassword = string.Empty;
                t.SftpPassword = string.Empty;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存存储目标失败：{ex.Message}";
        }
    }

    // =========================================================
    // 存储目标列表操作
    // =========================================================

    /// <summary>新增一个空目标。</summary>
    [RelayCommand]
    private void AddStorageTarget()
    {
        StorageTargets.Add(new StorageTargetForm
        {
            Kind = "Local",
            Name = $"target-{StorageTargets.Count + 1}",
            Enabled = true,
            IsPrimary = StorageTargets.Count == 0
        });
    }

    /// <summary>删除指定目标。</summary>
    [RelayCommand]
    private void RemoveStorageTarget(StorageTargetForm? target)
    {
        if (target is null) return;
        StorageTargets.Remove(target);

        // 若删除的是主目标，把第一个提升为主
        if (target.IsPrimary && StorageTargets.Count > 0)
            StorageTargets[0].IsPrimary = true;
    }

    /// <summary>复制一个目标（用于快速新增相似配置）。</summary>
    [RelayCommand]
    private void DuplicateStorageTarget(StorageTargetForm? target)
    {
        if (target is null) return;
        StorageTargets.Add(new StorageTargetForm
        {
            Kind = target.Kind,
            Name = $"{target.Name}-copy",
            Enabled = false,     // 默认不启用，避免误上传
            IsPrimary = false,
            LocalRoot = target.LocalRoot,
            FtpHost = target.FtpHost,
            FtpPort = target.FtpPort,
            FtpUser = target.FtpUser,
            SftpHost = target.SftpHost,
            SftpPort = target.SftpPort,
            SftpUser = target.SftpUser,
            SftpRoot = target.SftpRoot,
            RetryCount = target.RetryCount,
            CircuitBreakerThreshold = target.CircuitBreakerThreshold
        });
    }

    // =========================================================
    // 保存 - 采集 / 工作台
    // =========================================================

    [RelayCommand]
    private async Task SaveCollectAsync() => await SaveCoreAsync("collect", new Dictionary<string, string>
    {
        ["autoCollectOnConnect"] = Collect.AutoCollectOnConnect.ToString(),
        ["eraseAfterComplete"] = Collect.EraseAfterComplete.ToString(),
        ["skipCollected"] = Collect.SkipCollected.ToString(),
        ["collectAncillaryFiles"] = Collect.CollectAncillaryFiles.ToString()
    });

    [RelayCommand]
    private async Task SaveWorkbenchAsync() => await SaveCoreAsync("workbench", new Dictionary<string, string>
    {
        ["rows"] = Workbench.Rows.ToString(),
        ["columns"] = Workbench.Columns.ToString(),
        ["cardWidth"] = Workbench.CardWidth.ToString(),
        ["cardHeight"] = Workbench.CardHeight.ToString(),
        ["maxEmergencyTasks"] = Workbench.MaxEmergencyTasks.ToString()
    });

    private async Task SaveCoreAsync(string group, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            var hints = await _settings.UpdateAsync(group, values, "desktop");
            StatusMessage = hints.Count > 0 ? string.Join("；", hints) : "保存成功";
        }
        catch (Exception ex) { StatusMessage = $"保存失败：{ex.Message}"; }
    }

    // =========================================================
    // 网络（占位）
    // =========================================================

    [RelayCommand]
    private Task SaveNetworkAsync() => Task.CompletedTask;

    // =========================================================
    // 授权激活
    // =========================================================

    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseText))
        { StatusMessage = "请粘贴授权文件内容"; return; }

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
        catch (Exception ex) { StatusMessage = $"激活失败：{ex.Message}"; }
        finally { IsActivating = false; }
    }

    // =========================================================
    // 自检
    // =========================================================

    [RelayCommand]
    private async Task RunSelfCheckAsync()
    {
        IsSelfChecking = true;
        try
        {
            var items = await _selfCheck.RunAsync();
            SelfCheckItems.Clear();
            foreach (var item in items) SelfCheckItems.Add(item);
            SelfCheckTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex) { StatusMessage = $"自检失败：{ex.Message}"; }
        finally { IsSelfChecking = false; }
    }

    // =========================================================
    // 加密策略保存（新增 FileEncryption）
    // =========================================================

    [RelayCommand]
    private async Task SaveCryptoAsync()
    {
        if (!CanEdit) { StatusMessage = "无修改权限"; return; }
        try
        {
            await _cryptoPolicy.UpdateAsync(
                CryptoUsage.Password,
                new CryptoPolicyUpdate(Crypto.PasswordAlgorithm, null, null, Crypto.PasswordAllowLegacy),
                "desktop");

            await _cryptoPolicy.UpdateAsync(
                CryptoUsage.License,
                new CryptoPolicyUpdate(Crypto.LicenseEncrypt, Crypto.LicenseSign),
                "desktop");

            await _cryptoPolicy.UpdateAsync(
                CryptoUsage.FileSig,
                new CryptoPolicyUpdate(Crypto.FileSigDigest, Crypto.FileSigSign),
                "desktop");

            await _cryptoPolicy.UpdateAsync(
                CryptoUsage.SecretField,
                new CryptoPolicyUpdate(Crypto.SecretFieldAlgorithm),
                "desktop");

            // ★ 新增：文件加密算法
            await _cryptoPolicy.UpdateAsync(
                CryptoUsage.FileEncryption,
                new CryptoPolicyUpdate(Crypto.FileEncryptionAlgorithm),
                "desktop");

            StatusMessage = "加密策略已保存（后续写入生效，历史数据不自动迁移）";
            await LoadCryptoAuditsAsync();
        }
        catch (Exception ex) { StatusMessage = $"保存加密策略失败：{ex.Message}"; }
    }

    [RelayCommand]
    private async Task MigratePasswordsAsync()
    {
        if (!CanEdit) return;
        IsMigrating = true;
        try
        {
            var count = await _settings.MigratePasswordsAsync("desktop");
            StatusMessage = $"密码迁移完成，重哈希 {count} 条";
            await LoadCryptoAuditsAsync();
        }
        catch (Exception ex) { StatusMessage = $"密码迁移失败：{ex.Message}"; }
        finally { IsMigrating = false; }
    }

    [RelayCommand]
    private async Task RotateMasterKeyAsync()
    {
        if (!CanEdit) return;
        IsRotating = true;
        try
        {
            var result = await _keyRotation.RotateAsync("desktop");
            StatusMessage = $"主密钥轮换完成：v{result.OldVersion} → v{result.NewVersion}，" +
                            $"重加密 {result.ReEncryptedCount} 条，失败 {result.FailedCount} 条";
            await LoadCryptoAuditsAsync();
        }
        catch (Exception ex) { StatusMessage = $"主密钥轮换失败：{ex.Message}"; }
        finally { IsRotating = false; }
    }

    public void Dispose() { }
}

/// <summary>审计列表项。</summary>
public sealed class CryptoAuditDto
{
    public string Time { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}
