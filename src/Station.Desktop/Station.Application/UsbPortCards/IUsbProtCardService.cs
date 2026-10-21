using Station.Application.Collecting.Dtos;

namespace Station.Application.UsbPortCards;

/// <summary>工作台卡片快照服务。</summary>
public interface IUsbPortCardService
{
    /// <summary>获取全部槽位快照（长度 == ISlotRouter.Capacity）。</summary>
    Task<IReadOnlyList<UsbPortCardDto>> GetCurrentSnapshotAsync(CancellationToken ct = default);

    /// <summary>今日统计。</summary>
    Task<(int FileCount, long TotalBytes)> GetTodayStatsAsync(CancellationToken ct = default);

    /// <summary>发现模式数据源（设置页用）。</summary>
    Task<IReadOnlyList<UsbPortDiscoveryItemDto>> GetDiscoveryListAsync(CancellationToken ct = default);

    /// <summary>设备断开时清除磁盘空间缓存（避免显示旧数据）。</summary>
    void EvictSpaceCache(string deviceKey);
}

/// <summary>发现模式单条记录。</summary>
public sealed record UsbPortDiscoveryItemDto(
    string? PhysicalPortId,
    string? DeviceKey,
    string DeviceName,
    Contracts.ProtocolType? Protocol,
    string Root,
    int? DisplayPortNo,
    bool IsMapped,
    bool IsConnected);
