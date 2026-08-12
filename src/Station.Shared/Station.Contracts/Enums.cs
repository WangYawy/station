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
