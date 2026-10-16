using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Contracts;
using Station.Device.Detection;
using Station.Domain.Collecting;

namespace Station.Application.UsbPortCards;

/// <summary>
/// 工作台卡片快照服务。
/// 
/// 【职责】
///   1. 拉取 Device 层在线设备 + CollectTaskService 活跃任务；
///   2. 通过 ISlotRouter 把设备/任务路由到固定槽位；
///   3. 组装成固定 Capacity 个 UsbPortCardDto，供 UI 层快照刷新。
/// 
/// 【关键约定】
///   - 返回的列表长度 == ISlotRouter.Capacity（30）；
///   - 未分配设备 / 任务的槽位返回空 DTO（SlotIndex 有值，其他为 null/0）；
///   - 快照不带 Bound / Rejected：这些由 UI 事件流驱动；
///   - Version 从 ICollectTaskService.GetUiVersion 拉取，与事件同源。
/// </summary>
public sealed class UsbPortCardService : IUsbPortCardService
{
    private readonly IDevicePresenceService _devicePresence;
    private readonly ICollectTaskService _collectTaskService;
    private readonly ICollectFileQueryService _fileQuery;
    private readonly ISlotRouter _slotRouter;
    private readonly IUsbPortMapService _portMap;
    private readonly ILogger<UsbPortCardService> _logger;

    public UsbPortCardService(
        IDevicePresenceService devicePresence,
        ICollectTaskService collectTaskService,
        ICollectFileQueryService fileQuery,
        ISlotRouter slotRouter,
        IUsbPortMapService portMap,
        ILogger<UsbPortCardService> logger)
    {
        _devicePresence = devicePresence;
        _collectTaskService = collectTaskService;
        _fileQuery = fileQuery;
        _slotRouter = slotRouter;
        _portMap = portMap;
        _logger = logger;
    }

    // =========================================================
    // 快照
    // =========================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsbPortCardDto>> GetCurrentSnapshotAsync(
        CancellationToken ct = default)
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
            slots[slot.Value] = slots[slot.Value] with
            {
                DeviceKey = device.Key,
                DeviceName = device.Name,
                Protocol = device.Protocol,
                IsConnected = true,
                LinkState = device.LinkState,       // 只会是 Verifying
                PhysicalPortId = device.PhysicalPortId,
                DisplayPortNo = displayNo,

            };
        }

        // 3. 关联活跃任务
        var activeTasks = await _collectTaskService.GetActiveTasksAsync(ct);
        foreach (var task in activeTasks)
        {
            ct.ThrowIfCancellationRequested();

            var slot = _slotRouter.ResolveSlotForTask(task.TaskId);
            if (slot is null || slot.Value < 0 || slot.Value >= capacity)
            {
                // 任务未绑定槽位：等 UI 事件触发绑定后再快照
                continue;
            }

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

                OperatorUserId = task.OperatorUserId,
                OperatorName = task.OperatorUserName,
                OperatorDeptName = task.OperatorDeptName
            };
        }

        return slots;
    }

    // =========================================================
    // 统计
    // =========================================================

    /// <inheritdoc />
    public async Task<(int FileCount, long TotalBytes)> GetTodayStatsAsync(
        CancellationToken ct = default)
    {
        var files = await _fileQuery.GetTodayStatsAsync();
        var totalBytes = files.Sum(f => f.Size);
        return (files.Count, totalBytes);
    }

    // =========================================================
    // 发现模式（本轮只给骨架，具体面板另案）
    // =========================================================

    /// <inheritdoc />
    public Task<IReadOnlyList<UsbPortDiscoveryItemDto>> GetDiscoveryListAsync(
        CancellationToken ct = default)
    {
        var map = _portMap.GetMapping();
        var connected = _devicePresence.GetConnectedDevices();

        // 以"当前在线 + 已映射"合并生成发现列表
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<UsbPortDiscoveryItemDto>();

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

    // =========================================================
    // 内部
    // =========================================================

    private static int ResolveDisplayPortNo(
        string? physicalPortId, IReadOnlyDictionary<string, int> map)
    {
        if (string.IsNullOrEmpty(physicalPortId)) return 0;
        return map.TryGetValue(physicalPortId, out var no) ? no : 0;
    }
}
