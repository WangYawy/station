using Station.Contracts;
using Station.Domain.Enums;

namespace Station.Application.UsbPortCard;

/// <summary>
/// 工作台卡片的数据传输对象（用于快照查询）
/// </summary>
public sealed record UsbPortCardDto
{
    /// <summary>卡槽索引（从1开始）</summary>
    public int SlotIndex { get; init; }

    /// <summary>设备唯一标识（来自 IDevicePresenceService）</summary>
    public string? DeviceKey { get; init; }

    /// <summary>设备名称（记录仪名称）</summary>
    public string? DeviceName { get; init; }

    /// <summary>协议类型（UMS/MTP）</summary>
    public ProtocolType? Protocol { get; init; }

    /// <summary>物理设备是否在线</summary>
    public bool IsConnected { get; init; }

    /// <summary>关联的任务ID（无任务则为null）</summary>
    public long? TaskId { get; init; }

    /// <summary>任务编号</summary>
    public string? TaskNo { get; init; }

    /// <summary>任务状态</summary>
    public CollectTaskStatus? Status { get; init; }

    /// <summary>总文件数</summary>
    public int TotalFiles { get; init; }

    /// <summary>已采集文件数</summary>
    public int CollectedFiles { get; init; }

    /// <summary>总字节数</summary>
    public long TotalBytes { get; init; }

    /// <summary>已采集字节数</summary>
    public long CollectedBytes { get; init; }

    /// <summary>采集速度（字节/秒）</summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>是否紧急优先</summary>
    public bool IsEmergency { get; init; }

    /// <summary>任务开始时间</summary>
    public DateTime? StartedAt { get; init; }
}

/// <summary>
/// 端口卡片服务
/// </summary>
public interface IUsbPortCardService
{
    /// <summary>获取当前所有卡片的完整快照（用于启动加载和兜底刷新）</summary>
    Task<IReadOnlyList<UsbPortCardDto>> GetCurrentSnapshotAsync();

    /// <summary>获取今日采集统计（文件数和总大小）</summary>
    Task<(int FileCount, long TotalBytes)> GetTodayStatsAsync();

}
