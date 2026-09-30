namespace Station.Application;

/// <summary>
/// 采集站信息的不可变快照，供全局只读消费。
/// </summary>
public sealed record StationInfoDto(
    string StationUid,
    string StationCode,
    string StationName,
    string ProductSerialNumber,
    string Model,
    string OperatingSystem,
    int UsbPortCount,
    string Location,
    string Department,
    string ManagementMode)
{
    public const string ModeStandalone = "Standalone";
    public const string ModePlatform = "Platform";

    public bool IsPlatformMode =>
        string.Equals(ManagementMode, ModePlatform, StringComparison.OrdinalIgnoreCase);
}

public interface IStationInfoService
{
    /// <summary>当前采集站信息快照（只读，零数据库查询）</summary>
    StationInfoDto Current { get; }

    /// <summary>是否平台版（便捷属性）</summary>
    bool IsPlatformMode { get; }

    /// <summary>从数据库加载并刷新缓存；首次调用时若表中无记录则自动初始化</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>管理员在单机版界面保存信息时调用</summary>
    Task UpdateAsync(StationInfoDto info, CancellationToken ct = default);

    /// <summary>
    /// 平台版下发时调用。要求 <paramref name="platformVersion"/> 严格大于本地版本才会覆盖；
    /// OperatingSystem 始终保留本地已采集值。
    /// </summary>
    Task ApplyPlatformPushAsync(StationInfoDto info, long platformVersion,
        CancellationToken ct = default);

    /// <summary>信息变更事件（展示层可订阅以刷新 UI）</summary>
    event EventHandler<StationInfoDto>? InfoChanged;
}
