using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Security;
using Station.Application.Alerts;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Recorders;
using Station.Application.UsbPortCard.Events;
using Station.Contracts;
using Station.Domain.Collecting;
using Station.Domain.Entities;

namespace Station.Infrastructure.DeviceDetection;

/// <summary>
/// 记录仪接入监听核心：按 SourceMode 检测设备，连续稳定 N 次轮询视为接入（等挂载稳定），
/// 去重处理；设备拔出后清除跟踪，再次插入可重新触发。模拟源模式下不工作。
/// 同时实现 IDevicePresenceService 和 IHostedService，由容器统一管理单例生命周期。
/// </summary>
public sealed class RecorderConnectMonitor : IDevicePresenceService, IHostedService, IDisposable
{
    private readonly CollectOptions _options;
    private readonly IReadOnlyList<IRecorderDeviceDetector> _detectors;
    private readonly IUsbPortCardEventService _eventService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecorderConnectMonitor> _logger;
    private readonly int _settlePolls;

    // 线程安全的计数集合：Key -> 连续出现次数
    private readonly ConcurrentDictionary<string, int> _stable = new(StringComparer.Ordinal);
    // 线程安全的已连接设备集合：Key -> DetectedDevice
    private readonly ConcurrentDictionary<string, DetectedDevice> _connectedDevices = new(StringComparer.Ordinal);

    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// 构造函数：所有参数均由 DI 容器注入
    /// </summary>
    public RecorderConnectMonitor(
        CollectOptions options,
        IEnumerable<IRecorderDeviceDetector> detectors,
        IUsbPortCardEventService eventService,
        IServiceScopeFactory scopeFactory,
        ILogger<RecorderConnectMonitor> logger,
        int settlePolls = 2)
    {
        _options = options;
        _detectors = detectors.ToList();
        _eventService = eventService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settlePolls = Math.Max(1, settlePolls);
    }

    #region // ==================== IHostedService 实现 ====================

    /// <summary>
    /// 启动后台轮询（应用启动时自动调用）
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 立即执行一次检测，然后每 3 秒重复执行
        _timer = new Timer(
            _ => Check(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止后台轮询（应用关闭时自动调用）
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    #endregion

    #region // ==================== 核心检测逻辑 ====================

    public void Check()
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
                // 优先级：UMS > MTP
                selected = group
                    .OrderBy(d => d.Protocol == ProtocolType.Ums ? 0 :
                                   d.Protocol == ProtocolType.Mtp ? 1 : 2)
                    .First();

                // 生成统一 Key，确保设备切换模式时计数不中断
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
                        HandleConnectAsync(device);
                        //_eventService.PublishDeviceConnected(new DeviceConnectedEvent(key, device.Name, device.Protocol, device.Root));
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
    // ==================== 连接/断开事件处理（内部通过 Scope 解析服务） ====================

    /// <summary>
    /// 设备连接处理（内部使用 IServiceScopeFactory 创建作用域）
    /// </summary>
    private void HandleConnectAsync(DetectedDevice device)
    {
        // 第一步：立即触发物理接入事件（UI 瞬间响应）
        _eventService.PublishDeviceConnected(new DeviceConnectedEvent(
            device.Key, device.Name, device.Protocol, device.Root
        ));

        // 第二步：异步执行身份验证（不阻塞轮询线程）
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var identification = scope.ServiceProvider.GetRequiredService<IRecorderIdentificationService>();
                var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();
                var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

                var deviceInfo = new CollectDeviceInfo(device.Name, device.Serial, device.Protocol, RootPath: device.Root);
                var result = await identification.IdentifyAsync(deviceInfo, device.Root);

                // 处理验证结果，不需要配置文件或识别绑定成功的自动采集
                if (result.Status == RecorderIdentifyStatus.None || result.Status == RecorderIdentifyStatus.Bound)
                {
                    // 触发绑定成功事件
                    _eventService.PublishDeviceBound(new DeviceBoundEvent(
                        device.Key,
                        result.UserId!.Value,
                        result.DeptId!.Value
                    ));

                    // 自动采集逻辑
                    if (_options.AutoCollectOnConnect)
                    {
                        var bound = deviceInfo with { UserId = result.UserId, DeptId = result.DeptId };
                        var task = await collect.CreateTaskAsync(bound, isAuto: true);
                    }
                }
                else
                {
                    // 触发绑定失败事件
                    string reason = result.Status switch
                    {
                        RecorderIdentifyStatus.NoBinding => "设备未绑定，请联系管理员",
                        RecorderIdentifyStatus.InvalidSignature => "绑定文件被篡改，已拒绝接入",
                        RecorderIdentifyStatus.UnknownRecorder => "设备未授权",
                        _ => "未知错误"
                    };
                    _eventService.PublishDeviceRejected(new DeviceRejectedEvent(
                        device.Key, device.Name, reason
                    ));
                }
            }
            catch (Exception ex)
            {
                // 验证过程异常，触发失败事件
                _eventService.PublishDeviceRejected(new DeviceRejectedEvent(
                    device.Key, device.Name, $"验证异常：{ex.Message}"
                ));
                _logger.LogError("设备连接处理异常：{msg}", ex.Message);
            }
        });
    }

    /// <summary>
    /// 设备断开处理（内部使用 IServiceScopeFactory 创建作用域）
    /// </summary>
    private async Task HandleDisconnectAsync(DetectedDevice device)
    {
        using var scope = _scopeFactory.CreateScope();
        var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();

        // 自动中断该设备上正在运行的采集任务
        var activeTasks = await collect.GetActiveTasksAsync();
        var runningTask = activeTasks.FirstOrDefault(t => t.RecorderName == device.Name);
        if (runningTask is not null)
        {
            await collect.InterruptAsync(runningTask.TaskId, "记录仪物理断开");
        }
    }

    /// <summary>
    /// 安全触发断开回调（不阻塞轮询）
    /// </summary>
    private void TriggerDisconnect(DetectedDevice device)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleDisconnectAsync(device);
                _eventService.PublishDeviceDisconnected(new DeviceDisconnectedEvent(device.Key, device.Name));
            }
            catch
            {
                // 断开回调中的异常静默处理
            }
        });
    }
    #endregion

    // ==================== IDevicePresenceService 实现 ====================

    /// <summary>返回当前已稳定连接的设备列表（快照，线程安全）。</summary>
    public IReadOnlyList<DetectedDevice> GetConnectedDevices()
    {
        // Values 是线程安全的快照，但为了保险转为 List 返回副本
        return _connectedDevices.Values.ToList();
    }

    // ==================== IDisposable 实现 ====================

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _disposed = true;
    }
}
