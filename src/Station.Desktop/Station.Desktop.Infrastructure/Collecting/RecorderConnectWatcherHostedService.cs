using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Application.Collecting;
using Station.Application.Recorders;
using Station.Application.Alerts;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 记录仪接入后台监听：UMS/MTP 设备接入稳定后 → 识别归属（ini/台账）→
/// 已绑定且"接入自动采集"开启时自动创建并启动采集任务；未绑定/篡改/非授权由识别流程写报警。
/// </summary>
public sealed class RecorderConnectWatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CollectOptions _options;
    private readonly IReadOnlyList<IRecorderDeviceDetector> _detectors;
    private readonly ILogger<RecorderConnectWatcherHostedService> _logger;

    public RecorderConnectWatcherHostedService(
        IServiceScopeFactory scopeFactory,
        CollectOptions options,
        IEnumerable<IRecorderDeviceDetector> detectors,
        ILogger<RecorderConnectWatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _detectors = detectors.ToList();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var monitor = new RecorderConnectMonitor(_options, _detectors, HandleConnectAsync);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitor.CheckAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 单轮检测失败不中断后台服务
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleConnectAsync(DetectedDevice device)
    {
        using var scope = _scopeFactory.CreateScope();
        var identification = scope.ServiceProvider.GetRequiredService<IRecorderIdentificationService>();
        var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

        var deviceInfo = new CollectDeviceInfo(device.Name, device.Serial, device.Protocol, RootPath: device.Root);
        var result = await identification.IdentifyAsync(deviceInfo, device.Root);
        if (result.Status != RecorderIdentifyStatus.Bound)
        {
            _logger.LogWarning("记录仪接入 {Device}：识别未通过（{Status}：{Message}）", device.Name, result.Status, result.Message);
            // 未绑定/疑似篡改/非授权：识别流程已写报警，不自动采集
            return;
        }

        if (!_options.AutoCollectOnConnect)
        {
            return;
        }

        try
        {
            var bound = deviceInfo with { UserId = result.UserId, DeptId = result.DeptId };
            var task = await collect.CreateTaskAsync(bound, isAuto: true);
            _logger.LogInformation("记录仪接入 {Device}：已绑定，自动采集启动 {TaskNo}", device.Name, task.TaskNo);
            await alerts.WriteAsync(new Alert
            {
                Type = AlertType.UsbFault,
                Level = AlertLevel.Info,
                Title = "记录仪接入，自动采集已启动",
                Detail = $"{device.Name}（{task.TaskNo}）",
                Source = device.Key
            });
        }
        catch (Exception ex)
        {
            await alerts.WriteAsync(new Alert
            {
                Type = AlertType.UsbFault,
                Level = AlertLevel.Warning,
                Title = "自动采集启动失败",
                Detail = $"{device.Name}：{ex.Message}",
                Source = device.Key
            });
        }
    }
}
