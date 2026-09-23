using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// 
/// 【与 RecorderIdentifyResult 的对接】
///   - 枚举值：Matched / NotFound / Legacy / Tampered / RecorderMissing / UserInactive / DeptInactive / Mismatch
///   - 字段：User / Dept / Recorder 为实体对象，通过 .Id 取 ID
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

    #region IHostedService

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(
            _ => Check(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    #endregion

    #region 核心检测

    public void Check()
    {
        if (_options.SourceMode == "simulated") return;
        if (_detectors.Count == 0) return;

        // 1. 获取所有检测器返回的原始设备列表
        var raw = _detectors.SelectMany(d => d.Detect()).ToList();
        if (raw.Count == 0)
        {
            var allConnected = _connectedDevices.Values.ToList();
            _stable.Clear();
            _connectedDevices.Clear();
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
            if (_connectedDevices.ContainsKey(key)) continue;

            int newCount = _stable.AddOrUpdate(key, 1, (_, old) => old + 1);

            if (newCount >= _settlePolls)
            {
                var device = current.First(d => d.Key == key);
                if (_connectedDevices.TryAdd(key, device))
                {
                    try
                    {
                        HandleConnectAsync(device);
                    }
                    catch
                    {
                        _connectedDevices.TryRemove(key, out _);
                        _stable.TryRemove(key, out _);
                    }
                }
            }
        }

        // 5. 清理已拔出的设备
        var removedKeys = _stable.Keys.Where(k => !keys.Contains(k)).ToList();
        foreach (var key in removedKeys)
        {
            _stable.TryRemove(key, out _);
            if (_connectedDevices.TryRemove(key, out var disconnectedDevice))
            {
                TriggerDisconnect(disconnectedDevice);
            }
        }
    }

    #endregion

    #region 连接/断开事件处理

    /// <summary>
    /// 设备连接处理（内部使用 IServiceScopeFactory 创建作用域）。
    /// 
    /// 【新识别结果对接】
    ///   - 只有 Status == Matched 允许采集
    ///   - User / Dept 是实体对象，取 .Id 得 ID
    ///   - 其余状态按分类给出明确拒绝原因
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

                var deviceInfo = new CollectDeviceInfo(
                    device.Name, device.Serial, device.Protocol, RootPath: device.Root);

                // 传 CancellationToken.None（后台任务由外层管理生命周期）
                var result = await identification.IdentifyAsync(deviceInfo, device.Root, CancellationToken.None);

                // ============================================================
                // 识别通过：Matched 才允许采集
                // ============================================================
                if (result.Status == RecorderIdentifyStatus.Matched)
                {
                    var userId = result.User?.Id;
                    var deptId = result.Dept?.Id;

                    // 只有用户和部门都有效时才发布 Bound 事件
                    if (userId.HasValue && deptId.HasValue)
                    {
                        _eventService.PublishDeviceBound(new DeviceBoundEvent(
                            device.Key,
                            userId.Value,
                            deptId.Value
                        ));
                    }

                    // 自动采集逻辑
                    if (_options.AutoCollectOnConnect)
                    {
                        var bound = deviceInfo with
                        {
                            UserId = userId,
                            DeptId = deptId
                        };
                        await collect.CreateTaskAsync(bound, isAuto: true);
                    }
                }
                else
                {
                    // ============================================================
                    // 识别失败：按新枚举分类给出明确原因
                    // ============================================================
                    string reason = result.Status switch
                    {
                        RecorderIdentifyStatus.NotFound => "设备未绑定，请联系管理员",
                        RecorderIdentifyStatus.Legacy => "绑定文件格式已升级，请重新绑定记录仪",
                        RecorderIdentifyStatus.Tampered => "绑定文件被篡改，已拒绝接入",
                        RecorderIdentifyStatus.RecorderMissing => "记录仪不在台账或已停用",
                        RecorderIdentifyStatus.UserInactive => "绑定用户不在册或已禁用",
                        RecorderIdentifyStatus.DeptInactive => "绑定部门不在册或已禁用",
                        RecorderIdentifyStatus.Mismatch => "绑定信息与台账不一致",
                        _ => "未知错误"
                    };

                    _eventService.PublishDeviceRejected(new DeviceRejectedEvent(
                        device.Key, device.Name, reason
                    ));

                    _logger.LogWarning(
                        "设备 {Device} 识别拒绝：{Status} - {Message}",
                        device.Name, result.Status, result.Message ?? reason);
                }
            }
            catch (Exception ex)
            {
                _eventService.PublishDeviceRejected(new DeviceRejectedEvent(
                    device.Key, device.Name, $"验证异常：{ex.Message}"
                ));
                _logger.LogError(ex, "设备连接处理异常：{Device}", device.Name);
            }
        });
    }

    /// <summary>设备断开处理。</summary>
    private async Task HandleDisconnectAsync(DetectedDevice device)
    {
        using var scope = _scopeFactory.CreateScope();
        var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();

        var activeTasks = await collect.GetActiveTasksAsync();
        var runningTask = activeTasks.FirstOrDefault(t => t.RecorderName == device.Name);
        if (runningTask is not null)
        {
            await collect.InterruptAsync(runningTask.TaskId, "记录仪物理断开");
        }
    }

    /// <summary>安全触发断开回调（不阻塞轮询）。</summary>
    private void TriggerDisconnect(DetectedDevice device)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleDisconnectAsync(device);
                _eventService.PublishDeviceDisconnected(
                    new DeviceDisconnectedEvent(device.Key, device.Name));
            }
            catch
            {
                // 断开回调中的异常静默处理
            }
        });
    }

    #endregion

    #region IDevicePresenceService

    public IReadOnlyList<DetectedDevice> GetConnectedDevices()
        => _connectedDevices.Values.ToList();

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _disposed = true;
    }

    #endregion
}
