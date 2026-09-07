namespace Station.Contracts;

/// <summary>记录仪接入协议类型。</summary>
public enum ProtocolType
{
    /// <summary>USB Mass Storage（U 盘模式）。</summary>
    Ums = 0,

    /// <summary>MTP 设备。</summary>
    Mtp = 1,

    /// <summary>私有加密设备（SDK 转 UMS 模式）。</summary>
    PrivateSdk = 2,

    /// <summary>模拟开发/测试。</summary>
    Simulated = 999
}

/// <summary>文件类型。</summary>
public enum FileKind
{
    Video = 0,
    Audio = 1,
    Image = 2,
    Other = 3,
}

/// <summary>采集任务状态。</summary>
public enum TaskStatus
{
    Pending = 0,
    Scanning = 1,
    Collecting = 2,
    Verifying = 3,
    Uploading = 4,
    Completed = 5,
    Interrupted = 6,
    Failed = 7,
    Cancelled = 8,
}

/// <summary>报警级别。</summary>
public enum AlertLevel
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

/// <summary>报警处置状态。</summary>
public enum AlertStatus
{
    Pending = 0,
    Confirmed = 1,
    Processed = 2,
    Closed = 3,
}

/// <summary>授权状态。</summary>
public enum LicenseStatus
{
    Trial = 0,
    Grace = 1,
    Activated = 2,
    Locked = 3,
}

/// <summary>采集站运行状态（P1：维修/报废）。</summary>
public enum StationOperationalStatus
{
    Normal = 0,
    Maintenance = 1,
    Scrapped = 2,
}

/// <summary>报警类型。</summary>
public enum AlertType
{
    DiskLow = 0,
    NetworkDown = 1,
    UsbFault = 2,
    ChecksumFailed = 3,
    UnauthorizedAccess = 4,
    BindingInvalid = 5,
    StorageUnreachable = 6,
    LicenseExpired = 7,
}

/// <summary>远程指令类型（P0 六类）。</summary>
public enum CommandType
{
    RestartService = 0,
    ClearCache = 1,
    ReloadConfig = 2,
    RunSelfCheck = 3,
    StopCollecting = 4,
    StartCollecting = 5,

    /// <summary>写入记录仪绑定信息（平台台账重新绑定 → 采集站更新本地台账，接入时自动写 ini）。</summary>
    WriteBinding = 6,
}

/// <summary>远程指令执行状态。</summary>
public enum CommandStatus
{
    Pending = 0,
    Pulled = 1,
    Executing = 2,
    Succeeded = 3,
    Failed = 4,
    Timeout = 5,
}

/// <summary>配置同步操作类型。</summary>
public enum SyncOperation
{
    Upsert = 0,
    Delete = 1,
}
