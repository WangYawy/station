using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.FileSystem;
using Station.Domain.Collecting;

namespace Station.Application.UsbPortCards;

/// <summary>
/// 工作台卡片快照服务。
/// 
/// 【职责】
///   1. 拉取 Device 层在线设备 + CollectTaskService 活跃任务；
///   2. 通过 ISlotRouter 把设备/任务路由到固定槽位；
///   3. 组装成固定 Capacity 个 UsbPortCardDto，供 UI 层快照刷新；
///   4. 设备磁盘空间按协议从 IDeviceFileSystem.GetDeviceSpace 读取（15 秒缓存）。
/// 
/// 【关键约定】
///   - 返回的列表长度 == ISlotRouter.Capacity（默认 30）；
///   - 未分配设备 / 任务的槽位返回空 DTO（SlotIndex 有值，其他为 null/0）；
///   - 快照 LinkState 只携带物理态（Offline / Verifying）；
///     Bound / Rejected 由 UI 层从业务事件获得，快照不覆盖业务态；
///   - Version 从 ICollectTaskService.GetUiVersion 拉取，与事件同源；
///   - 设备断开时调用 EvictSpaceCache(deviceKey) 主动清空间缓存。
/// </summary>
public sealed class UsbPortCardService : IUsbPortCardService
{
    private readonly IDevicePresenceService _devicePresence;
    private readonly ICollectTaskService _collectTaskService;
    private readonly ICollectFileQueryService _fileQuery;
    private readonly ISlotRouter _slotRouter;
    private readonly IUsbPortMapService _portMap;
    private readonly IDeviceFileSystemProvider _fileSystems;
    private readonly ILogger<UsbPortCardService> _logger;

    /// <summary>
    /// 设备空间缓存：DeviceKey → 空间快照。
    /// MTP 空间查询较慢（WPD COM / libmtp 链表遍历），缓存 15 秒避免 5 秒心跳每次重查。
    /// </summary>
    private readonly ConcurrentDictionary<string, SpaceSnapshot> _spaceCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan SpaceCacheTtl = TimeSpan.FromSeconds(15);

    private readonly record struct SpaceSnapshot(
        long? Available, long? Total, DateTime CachedAt);

    public UsbPortCardService(
        IDevicePresenceService devicePresence,
        ICollectTaskService collectTaskService,
        ICollectFileQueryService fileQuery,
        ISlotRouter slotRouter,
        IUsbPortMapService portMap,
        IDeviceFileSystemProvider fileSystems,
        ILogger<UsbPortCardService> logger)
    {
        _devicePresence = devicePresence;
        _collectTaskService = collectTaskService;
        _fileQuery = fileQuery;
        _slotRouter = slotRouter;
        _portMap = portMap;
        _fileSystems = fileSystems;
        _logger = logger;
    }

    // ========== 快照 ==========

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsbPortCardDto>> GetCurrentSnapshotAsync(CancellationToken ct = default)
    {
        var capacity = Math.Max(_slotRouter.Capacity, 1);
        var map = _portMap.GetMapping();

        // 1. 初始化：所有槽位为空
        var slots = new UsbPortCardDto[capacity];
        for (var i = 0; i < capacity; i++)
        {
            slots[i] = new UsbPortCardDto
            {
                SlotIndex = i,
                LinkState = DeviceLinkState.Offline
            };
        }

        // 2. 处理物理在线设备 → 路由到槽位
        var connected = _devicePresence.GetConnectedDevices();
        foreach (var device in connected)
        {
            var slot = _slotRouter.ResolveSlotForDevice(device.Key);
            if (slot is null || slot.Value < 0 || slot.Value >= capacity)
            {
                // Fixed 模式下未映射 / Pooled 模式下满员 → 跳过
                _logger.LogDebug(
                    "设备 {DeviceKey} 未映射到槽位（PhysicalPortId={PortId}）",
                    device.Key, device.PhysicalPortId);
                continue;
            }
           
            var displayNo = ResolveDisplayPortNo(device.PhysicalPortId, map);
            var (availBytes, totalBytes) = ResolveDeviceSpace(device);

            slots[slot.Value] = slots[slot.Value] with
            {
                DeviceKey = device.Key,
                DeviceName = device.Name,
                Protocol = device.Protocol,
                IsConnected = true,
                LinkState = device.LinkState,       // 只会是 Verifying
                PhysicalPortId = device.PhysicalPortId,
                DisplayPortNo = displayNo,

                DeviceAvailableBytes = availBytes,
                DeviceTotalBytes = totalBytes
            };
        }

        // 3. 关联活跃任务
        var activeTasks = await _collectTaskService.GetActiveTasksAsync(ct);
        foreach (var task in activeTasks)
        {
            ct.ThrowIfCancellationRequested();

            // ---- 3.1 槽位定位：先查绑定，再按物理端口兜底 ----
            var slot = _slotRouter.ResolveSlotForTask(task.TaskId);
            if (slot is null && !string.IsNullOrEmpty(task.PhysicalPortId))
            {
                // 兜底：从任务快照的 PhysicalPortId 反推 DeviceKey 再查槽位
                var deviceKey = "PORT:" + task.PhysicalPortId.Trim().ToUpperInvariant();
                slot = _slotRouter.ResolveSlotForDevice(deviceKey);
                if (slot is not null)
                {
                    _slotRouter.BindTask(task.TaskId, slot.Value);
                    _logger.LogDebug(
                        "任务 {TaskId} 按端口 {PortId} 兜底绑定到槽 {Slot}",
                        task.TaskId, task.PhysicalPortId, slot.Value);
                }
            }
            if (slot is null || slot.Value < 0 || slot.Value >= capacity)
            {
                // 任务未绑定槽位且无法兜底：等 UI 事件触发绑定后再快照
                _logger.LogDebug("任务 {TaskId} 未绑定槽位且兜底失败", task.TaskId);
                continue;
            }

            // ---- 3.2 填充任务字段 ----
            var current = slots[slot.Value];
            // 版本号：与事件流同源，避免旧快照覆盖新事件
            var version = _collectTaskService.GetUiVersion(task.TaskId);

            slots[slot.Value] = current with
            {
                // 任务字段
                TaskId = task.TaskId,
                TaskNo = task.TaskNo,
                Status = task.Status,
                TotalFiles = task.TotalFiles,
                CollectedFiles = task.CollectedFiles,
                TotalBytes = task.TotalBytes,
                CollectedBytes = task.CollectedBytes,
                SpeedBytesPerSecond = task.SpeedBytesPerSecond,
                IsEmergency = task.IsEmergency,
                StartedAt = task.StartedAt,
                Version = version,

                // 设备字段：槽位已有则保留；缺失则用任务信息兜底
                DeviceKey = current.DeviceKey,
                DeviceName = current.DeviceName ?? task.RecorderName,
                Protocol = current.Protocol ?? task.Protocol,

                // 操作人
                OperatorUserId = task.OperatorUserId,
                OperatorName = task.OperatorUserName,
                OperatorDeptName = task.OperatorDeptName,

                // 端口（若快照中设备尚未对齐，用任务端口补）
                PhysicalPortId = current.PhysicalPortId ?? task.PhysicalPortId

            };
        }

        return slots;
    }

    // ========== 统计 ========== 

    /// <inheritdoc />
    public async Task<(int FileCount, long TotalBytes)> GetTodayStatsAsync(
        CancellationToken ct = default)
    {
        var files = await _fileQuery.GetTodayStatsAsync();
        var totalBytes = files.Sum(f => f.Size);
        return (files.Count, totalBytes);
    }

    // ========== 发现模式（本轮只给骨架，具体面板另案） ============
    /// <inheritdoc />
    public Task<IReadOnlyList<UsbPortDiscoveryItemDto>> GetDiscoveryListAsync(
        CancellationToken ct = default)
    {
        var map = _portMap.GetMapping();
        var connected = _devicePresence.GetConnectedDevices();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<UsbPortDiscoveryItemDto>();

        // 已连接设备
        foreach (var device in connected)
        {
            var portId = device.PhysicalPortId;
            if (string.IsNullOrEmpty(portId)) continue;

            seen.Add(portId);
            var mapped = map.TryGetValue(portId, out var no);
            items.Add(new UsbPortDiscoveryItemDto(
                PhysicalPortId: portId,
                DeviceKey: device.Key,
                DeviceName: device.Name,
                Protocol: device.Protocol,
                Root: device.Root,
                DisplayPortNo: mapped ? no : null,
                IsMapped: mapped,
                IsConnected: true));
        }

        // 已映射但当前离线
        foreach (var kv in map)
        {
            if (seen.Contains(kv.Key)) continue;
            items.Add(new UsbPortDiscoveryItemDto(
                PhysicalPortId: kv.Key,
                DeviceKey: null,
                DeviceName: "-- 未插入 --",
                Protocol: null,
                Root: string.Empty,
                DisplayPortNo: kv.Value,
                IsMapped: true,
                IsConnected: false));
        }

        // 排序：已连接优先，按 DisplayPortNo 升序
        items.Sort((a, b) =>
        {
            if (a.IsConnected != b.IsConnected) return a.IsConnected ? -1 : 1;
            var aNo = a.DisplayPortNo ?? int.MaxValue;
            var bNo = b.DisplayPortNo ?? int.MaxValue;
            return aNo.CompareTo(bNo);
        });

        return Task.FromResult<IReadOnlyList<UsbPortDiscoveryItemDto>>(items);
    }


    /// <summary>设备断开时主动清除空间缓存（避免显示旧磁盘数据）。</summary>
    public void EvictSpaceCache(string deviceKey)
    {
        if (string.IsNullOrEmpty(deviceKey)) return;
        _spaceCache.TryRemove(deviceKey, out _);
    }

    // ==========  内部 ==========

    private static int ResolveDisplayPortNo(
        string? physicalPortId, IReadOnlyDictionary<string, int> map)
    {
        if (string.IsNullOrEmpty(physicalPortId)) return 0;
        return map.TryGetValue(physicalPortId, out var no) ? no : 0;
    }

    /// <summary>
    /// 读取设备空间，带 15 秒缓存。
    /// 缓存命中直接返回；过期或未命中时调 IDeviceFileSystem.GetDeviceSpace。
    /// 失败也缓存（避免频繁重试慢速 MTP 设备）。
    /// </summary>
    private (long? Available, long? Total) ResolveDeviceSpace(DetectedDevice device)
    {
        if (_spaceCache.TryGetValue(device.Key, out var snap)
            && DateTime.UtcNow - snap.CachedAt < SpaceCacheTtl)
        {
            return (snap.Available, snap.Total);
        }

        (long? Available, long? Total) result;
        try
        {
            var fs = _fileSystems.GetFor(device.Protocol);
            result = fs.GetDeviceSpace(device);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取设备空间失败：{DeviceKey}", device.Key);
            result = (null, null);
        }

        _spaceCache[device.Key] = new SpaceSnapshot(result.Available, result.Total, DateTime.UtcNow);
        return result;
    }
}
