using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Application.Notifications;
using Station.Application.Recorders;
using Station.Device.Detection;
using Station.Device.Events;

namespace Station.Application.DeviceIntegration;

/// <summary>
/// 设备事件出口：Application 层唯一的物理 → 业务粘合点。
///
/// 【识别后的处理分支（混合续采策略）】
///   Matched 且找到可续采任务（同端口 Paused/Interrupted/Failed）
///     → 不建新任务
///     → AutoResumeInterruptedOnReconnect = true 时自动 ResumeAsync
///     → 否则卡片显示"已中断/已暂停"，等用户点"重试"
///   Matched 且无可续采任务
///     → AutoCollectOnConnect = true 时 CreateTaskAsync（建新任务）
///     → 否则卡片显示"已连接"，等用户手动开始
/// </summary>
public sealed class ApplicationDeviceEventSink : IDeviceEventSink
{
    private readonly IUiNotificationService _ui;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CollectOptions _collectOptions;
    private readonly ILogger<ApplicationDeviceEventSink> _logger;

    private long _deviceVersion = 0;

    public ApplicationDeviceEventSink(
        IUiNotificationService ui,
        IServiceScopeFactory scopeFactory,
        IOptions<CollectOptions> collectOptions,
        ILogger<ApplicationDeviceEventSink> logger)
    {
        _ui = ui;
        _scopeFactory = scopeFactory;
        _collectOptions = collectOptions.Value;
        _logger = logger;
    }

    // ========== IDeviceEventSink ==========
    public void PublishConnected(DeviceConnectedEvent e)
    {
        _ui.PublishDeviceConnected(e);
        _ = Task.Run(() => IdentifyAsync(e));
    }

    public void PublishDisconnected(DeviceDisconnectedEvent e)
    {
        _ = Task.Run(() => OnDisconnectedAsync(e));
        _ui.PublishDeviceDisconnected(e);
    }

    // ========== 识别 ==========

    private async Task IdentifyAsync(DeviceConnectedEvent e)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var identification = scope.ServiceProvider
                .GetRequiredService<IRecorderIdentificationService>();
            var collect = scope.ServiceProvider
                .GetRequiredService<ICollectTaskService>();

            var device = new DetectedDevice(
                Key: e.DeviceKey,
                Name: e.DeviceName,
                Serial: null,
                Protocol: e.Protocol,
                Root: e.Root,
                PhysicalPortId: e.PhysicalPortId);

            var result = await identification.IdentifyAsync(device, CancellationToken.None);

            if (result.Status == RecorderIdentifyStatus.Matched)
                await HandleMatchedAsync(device, result, collect);
            else
                HandleRejected(device, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备识别异常：{Device}", e.DeviceName);

            _ui.PublishDeviceRejected(new DeviceRejectedEvent(
                e.DeviceKey, e.DeviceName,
                $"验证异常：{ex.Message}", e.PhysicalPortId, NextVersion()));
        }
    }

    private async Task HandleMatchedAsync(
        DetectedDevice device, RecorderIdentifyResult result, ICollectTaskService collect)
    {
        var userId = result.Device.UserId;
        var deptId = result.Device.DeptId;

        // 1. 发 Bound 事件（业务态）
        if (userId.HasValue && deptId.HasValue)
        {
            _ui.PublishDeviceBound(new DeviceBoundEvent(
                device.Key, userId.Value, deptId.Value, device.PhysicalPortId,
                result.Device.UserName, result.Device.DeptName, NextVersion()));

            _logger.LogInformation(
                "设备识别通过 {Device}：用户 {User} / 部门 {Dept}",
                device.Name, userId.Value, deptId.Value);
        }

        // 2. 查可续采任务
        var resumeable = await collect.FindResumeableTaskAsync(device.PhysicalPortId);

        if (resumeable is not null)
        {
            _logger.LogInformation(
                "设备 {Device} 接入：发现可续采任务 {TaskNo}（{Status}）",
                device.Name, resumeable.TaskNo, resumeable.Status);

            if (_collectOptions.AutoResumeInterruptedOnReconnect)
            {
                try
                {
                    await collect.ResumeAsync(resumeable.TaskId);
                    _logger.LogInformation(
                        "自动续采任务 {TaskNo}", resumeable.TaskNo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "自动续采失败 {TaskNo}，等待用户手动重试", resumeable.TaskNo);
                }
            }

            // 无论是否自动续采，都不建新任务
            return;
        }

        // 3. 无可续采任务：走正常自动采集
        if (_collectOptions.AutoCollectOnConnect)
        {
            await collect.CreateTaskAsync(result.Device, isAuto: true);
        }
    }

    private void HandleRejected(DetectedDevice device, RecorderIdentifyResult result)
    {
        var reason = result.Message ?? MapReason(result.Status);
        _ui.PublishDeviceRejected(new DeviceRejectedEvent(
            device.Key, device.Name, reason, device.PhysicalPortId, NextVersion()));

        _logger.LogWarning("设备 {Device} 识别拒绝：{Status} - {Message}",
            device.Name, result.Status, reason);
    }

    // ========== 断开 ==========

    private async Task OnDisconnectedAsync(DeviceDisconnectedEvent e)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();

            var activeTasks = await collect.GetActiveTasksAsync();
            var running = activeTasks.FirstOrDefault(t =>
                string.Equals(t.RecorderName, e.DeviceName, StringComparison.Ordinal));

            if (running is not null)
            {
                await collect.InterruptAsync(running.TaskId, "记录仪物理断开");
                _logger.LogInformation("设备 {Device} 断开，已中断任务 {TaskNo}",
                    e.DeviceName, running.TaskNo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "断开处理异常：{Device}", e.DeviceName);
        }
    }

    // =========================================================
    // 辅助
    // =========================================================

    private long NextVersion() => Interlocked.Increment(ref _deviceVersion);

    private static string MapReason(RecorderIdentifyStatus s) => s switch
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
}
