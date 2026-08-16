namespace Station.Application.Settings;

/// <summary>基本设置（本机编号只读，运行模式切换后重启生效）。</summary>
public sealed record BasicSettingsDto(
    string StationNo,
    string InstallLocation,
    string RunMode,
    string PlatformBaseUrl,
    string PlatformStationCode);

/// <summary>存储策略。</summary>
public sealed record StorageSettingsDto(
    string Target,
    string LocalRoot,
    string DirectoryTemplate,
    string FtpHost,
    int FtpPort,
    string FtpUser,
    string SftpHost,
    int SftpPort,
    string SftpUser,
    string SftpRoot,
    int CircuitBreakerThreshold,
    int CircuitBreakerCooldownSeconds,
    int VideoRetentionDays,
    int LogRetentionDays,
    string CleanupTime);

/// <summary>采集策略。</summary>
public sealed record CollectSettingsDto(
    bool AutoCollectOnConnect,
    bool EraseAfterComplete,
    bool SkipCollected,
    bool CollectAncillaryFiles);

/// <summary>工作台显示（卡片布局 + 紧急优先上限）。</summary>
public sealed record WorkbenchSettingsDto(
    int Rows,
    int Columns,
    int CardWidth,
    int CardHeight,
    int MaxEmergencyTasks);

/// <summary>授权状态（只读）。</summary>
public sealed record LicenseSettingsDto(
    string Status,
    string Message,
    DateTime? ExpiresAt,
    int DaysLeft);

/// <summary>设备自检单项结果。</summary>
public sealed record SelfCheckItemDto(string Name, bool Ok, string Detail);

/// <summary>设置查询结果（网络组由 Web 宿主/桌面端补充，授权为只读展示）。</summary>
public sealed record SystemSettingsCoreDto(
    BasicSettingsDto Basic,
    StorageSettingsDto Storage,
    CollectSettingsDto Collect,
    WorkbenchSettingsDto Workbench,
    LicenseSettingsDto License,
    bool ReadOnly);

/// <summary>设置更新请求：分组内键值（仅提交要修改的键）。</summary>
public sealed record SystemSettingsUpdateRequest(string Group, IReadOnlyDictionary<string, string> Values);
