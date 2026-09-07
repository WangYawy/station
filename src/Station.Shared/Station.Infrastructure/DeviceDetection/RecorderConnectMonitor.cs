using System.Collections.Concurrent;
using Renci.SshNet.Security;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.UsbPortCard.Events;
using Station.Contracts;

namespace Station.Infrastructure.DeviceDetection;

/// <summary>
/// 记录仪接入监听核心：按 SourceMode 检测设备，连续稳定 N 次轮询视为接入（等挂载稳定），
/// 去重处理；设备拔出后清除跟踪，再次插入可重新触发。模拟源模式下不工作。
/// </summary>
public sealed class RecorderConnectMonitor : IDevicePresenceService
{
    private readonly CollectOptions _options;
    private readonly IReadOnlyList<IRecorderDeviceDetector> _detectors;
    private readonly Func<DetectedDevice, Task> _connectHandler;
    private readonly Func<DetectedDevice, Task> _disconnectHandler;
    private readonly int _settlePolls;
    private readonly ConcurrentDictionary<string, int> _stable = new(StringComparer.Ordinal); // 线程安全的计数集合：Key -> 连续出现次数
    private readonly ConcurrentDictionary<string, DetectedDevice> _connectedDevices = new(StringComparer.Ordinal); // 线程安全的已连接设备集合：Key -> DetectedDevice（触发 _handler 后加入）
    private readonly IUsbPortCardEventService _eventService;

    public RecorderConnectMonitor(
        CollectOptions options,
        IReadOnlyList<IRecorderDeviceDetector> detectors,
        Func<DetectedDevice, Task> connectHandler,
        Func<DetectedDevice, Task> disconnectHandler,
        IUsbPortCardEventService eventService,
        int settlePolls = 2)
    {
        _options = options;
        _detectors = detectors;
        _connectHandler = connectHandler;
        _disconnectHandler = disconnectHandler;
        _eventService = eventService;
        _settlePolls = Math.Max(1, settlePolls);
    }

    public async Task CheckAsync()
    {
        if (_options.SourceMode == "simulated")
        {
            return;
        }

        if (_detectors.Count == 0)
        {
            return;
        }

        // 1. 获取所有检测器返回的原始设备列表
        var raw = _detectors.SelectMany(d => d.Detect()).ToList();
        if (raw.Count == 0)
        {
            // 取出所有已连接设备，清空集合
            var allConnected = _connectedDevices.Values.ToList();
            _stable.Clear();
            _connectedDevices.Clear();
            // 逐个触发断开回调
            foreach (var device in allConnected)
            {
                TriggerDisconnect(device);
            }
            return;
        }
        // 2. 按设备名称（不区分大小写）分组，处理同一物理设备的多种协议
        var groups = raw.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var current = new List<DetectedDevice>();
        foreach (var group in groups)
        {
            DetectedDevice selected;
            if (group.Count() == 1)
            {
                selected = group.First();
            }
            else
            {
                // 优先级：UMS > MTP（可配置）
                selected = group
                    .OrderBy(d => d.Protocol == ProtocolType.Ums ? 0 :
                                   d.Protocol == ProtocolType.Mtp ? 1 : 2)
                    .First();

                // 生成统一 Key，确保设备切换模式时计数不中断
                // 保留原始协议信息（Protocol 属性不变）
                selected = selected with { Key = "UNIFIED:" + group.Key };
            }
            current.Add(selected);
        }
        // 3. 当前所有有效 Key
        var keys = current.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        // 4. 稳定计数与触发
        foreach (var key in keys)
        {
            // 如果已经连接，跳过（不再重复触发）
            if (_connectedDevices.ContainsKey(key))
                continue;
            // 累加稳定计数（线程安全）
            int newCount = _stable.AddOrUpdate(key, 1, (_, old) => old + 1);

            if (newCount >= _settlePolls)
            {
                // 只有第一次达到阈值时才加入连接集合，避免重复
                var device = current.First(d => d.Key == key);
                if (_connectedDevices.TryAdd(key, device))
                {
                    try
                    {
                        await _connectHandler(device);
                        _eventService.PublishDeviceConnected(new DeviceConnectedEvent(key, device.Name, device.Protocol));
                    }
                    catch
                    {
                        // 处理失败时，移除连接记录，允许下次重试
                        _connectedDevices.TryRemove(key, out _);
                        // 同时重置稳定计数，避免下次一出现就触发（可选）
                        _stable.TryRemove(key, out _);
                    }
                }
            }
        }

        // 5. 清理已拔出的设备（Key 不在当前列表中）
        var removedKeys = _stable.Keys.Where(k => !keys.Contains(k)).ToList();
        foreach (var key in removedKeys)
        {
            // 从稳定计数中移除
            _stable.TryRemove(key, out _);

            // 尝试从已连接列表中移除，如果存在则触发断开回调
            if (_connectedDevices.TryRemove(key, out var disconnectedDevice))
            {
                TriggerDisconnect(disconnectedDevice);
            }
        }
    }

    /// <summary>返回当前已稳定连接的设备列表（快照，线程安全）。</summary>
    public IReadOnlyList<DetectedDevice> GetConnectedDevices()
    {
        // Values 是线程安全的快照，但为了保险转为 List 返回副本
        return _connectedDevices.Values.ToList();
    }


    // 私有辅助方法：安全触发断开回调（不阻塞轮询）
    private void TriggerDisconnect(DetectedDevice device)
    {
        if (_disconnectHandler is null) return;

        // 使用 Task.Run 异步执行，避免回调中的 I/O 阻塞轮询线程
        _ = Task.Run(async () =>
        {
            try
            {
                await _disconnectHandler(device);
                _eventService.PublishDeviceDisconnected(new DeviceDisconnectedEvent(device.Key, device.Name));
            }
            catch
            {
                // 断开回调中的异常在此静默处理（可由回调内部自行记录日志）
                // 如果希望记录日志，可通过构造函数额外注入 ILogger，这里为了简洁先忽略
            }
        });
    }
}
