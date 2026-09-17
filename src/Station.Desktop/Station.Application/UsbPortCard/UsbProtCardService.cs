using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Settings;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;

namespace Station.Application.UsbPortCard;

public sealed class UsbPortCardService : IUsbPortCardService
{
    private readonly IDevicePresenceService _devicePresence;
    private readonly ICollectTaskService _collectTaskService;
    private readonly IRepository<CollectFile> _fileRepository;
    private readonly WindowModeOptions _options;

    public UsbPortCardService(
        IDevicePresenceService devicePresence,
        ICollectTaskService collectTaskService,
        IRepository<CollectFile> fileRepository,
        IOptions<WindowModeOptions> options)
    {
        _devicePresence = devicePresence;
        _collectTaskService = collectTaskService;
        _fileRepository = fileRepository;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<UsbPortCardDto>> GetCurrentSnapshotAsync()
    {
        // 1. 获取物理在线设备（内存，零IO）
        var connectedDevices = _devicePresence.GetConnectedDevices();

        // 2. 获取活跃任务（数据库查询）
        var activeTasks = await _collectTaskService.GetActiveTasksAsync();

        // 3. 构建 设备Key → 任务 的映射
        //    使用同一套 Key 生成规则（与 RecorderConnectMonitor 保持一致）
        var taskByDeviceKey = new Dictionary<string, CollectTaskDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in activeTasks)
        {
            var key = BuildDeviceKey(task);
            if (!taskByDeviceKey.ContainsKey(key))
                taskByDeviceKey[key] = task;
        }

        // 4. 遍历固定数量的卡槽，生成 DTO
        var totalSlots = Math.Clamp(_options.Rows * _options.Columns, 1, 100);
        var results = new List<UsbPortCardDto>(totalSlots);
        var deviceList = connectedDevices.ToList();

        for (int i = 0; i < totalSlots; i++)
        {
            var device = i < deviceList.Count ? deviceList[i] : null;
            CollectTaskDto? matchedTask = null;

            if (device is not null)
            {
                // 尝试匹配任务
                if (taskByDeviceKey.TryGetValue(device.Key, out var task))
                    matchedTask = task;
            }

            results.Add(new UsbPortCardDto
            {
                SlotIndex = i + 1,
                DeviceKey = device?.Key,
                DeviceName = device?.Name ?? "-- 等待设备连接",
                Protocol = device?.Protocol,
                IsConnected = device is not null,
                TaskId = matchedTask?.TaskId,
                TaskNo = matchedTask?.TaskNo,
                Status = matchedTask?.Status,
                TotalFiles = matchedTask?.TotalFiles ?? 0,
                CollectedFiles = matchedTask?.CollectedFiles ?? 0,
                TotalBytes = matchedTask?.TotalBytes ?? 0,
                CollectedBytes = matchedTask?.CollectedBytes ?? 0,
                SpeedBytesPerSecond = matchedTask?.SpeedBytesPerSecond ?? 0,
                IsEmergency = matchedTask?.IsEmergency ?? false,
                StartedAt = matchedTask?.StartedAt
            });
        }

        return results;
    }
    public async Task<(int FileCount, long TotalBytes)> GetTodayStatsAsync()
    {
        var today = DateTime.Today;
        var files = await _fileRepository.GetListAsync(f =>
            f.Status == CollectFileStatus.Completed && f.CollectedAt >= today);
        var totalBytes = files.Sum(f => f.Size);
        return (files.Count, totalBytes);
    }

    /// <summary>
    /// 根据任务信息构建设备 Key，必须与 IRecorderDeviceDetector 生成的 Key 规则一致
    /// </summary>
    private static string BuildDeviceKey(CollectTaskDto task)
    {
        // 如果有序列号，优先使用；否则使用名称
        if (!string.IsNullOrEmpty(task.RecorderSerial))
        {
            return task.Protocol == ProtocolType.Mtp
                ? $"MTP:{task.RecorderSerial}"
                : $"UMS:{task.RecorderSerial}";
        }

        // 模拟源或序列号缺失的情况
        return $"UNIFIED:{task.RecorderName}";
    }
}
