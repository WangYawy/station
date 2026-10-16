using Microsoft.Extensions.Logging;
using Station.Application.Alerts.Dtos;
using Station.Application.Alerts;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Data.IdGeneration;
using Station.Domain.Alerts;

namespace Station.Application.Alerts;

public sealed class AlertService : IAlertService
{
    private readonly IAlertRepository _alerts;
    private readonly IAlertQueryService _alertQuery;
    private readonly IIdGenerator _idGenerator;
    private readonly ISyncOutboxService _outbox;
    private readonly ReportingOptions _reportingOptions;
    private readonly IStationContext _stationContext;
    private readonly ILogger<AlertService> _logger;
    private readonly ILicenseCryptoService _licenseCrypto;

    public AlertService(
        IAlertRepository alerts,
        IAlertQueryService alertQuery,
        IIdGenerator idGenerator,
        ISyncOutboxService outbox,
        ReportingOptions reportingOptions,
        IStationContext stationContext,
        ILicenseCryptoService licenseCrypto,
        ILogger<AlertService> logger)
    {
        _alerts = alerts;
        _alertQuery = alertQuery;
        _idGenerator = idGenerator;
        _outbox = outbox;
        _reportingOptions = reportingOptions;
        _stationContext = stationContext;
        _logger = logger;
        _licenseCrypto = licenseCrypto;
    }

    /// <summary>报警产生入口。参数使用原始字段，避免外部构造聚合。</summary>
    public async Task WriteAsync(
        AlertType type, AlertLevel level, string title,
        string? detail = null, string? source = null, long? deptId = null)
    {
        var alert = Alert.Create(
            _idGenerator.NewId(), type, level, title,
            detail, source, deptId, DateTime.Now);

        await _alerts.AddAsync(alert);
        _logger.LogInformation("报警写入 {Type}/{Level}：{Title}（来源 {Source}）",
            alert.Type, alert.Level, alert.Title, alert.Source);

        // 上报平台
        if (_stationContext.StationId is { } stationId)
        {
            var report = new AlertReport
            {
                StationId = stationId,
                LocalAlertId = alert.Id,
                Type = alert.Type,
                Level = alert.Level,
                Source = alert.Source ?? string.Empty,
                Message = alert.Detail ?? alert.Title,
                OccurredAt = alert.CreatedAt,
                Signature = null
            };

            if (!string.IsNullOrWhiteSpace(_reportingOptions.PrivateKeyFile))
            {
                var canonical = AlertReportSignature.Canonical(report);
                var (signature, _) = await _licenseCrypto.SignReportingAsync(canonical);
                if (signature is not null)
                    report = report with { Signature = signature };
            }

            await _outbox.EnqueueAsync("alert",
                System.Text.Json.JsonSerializer.Serialize(report));
        }
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        AlertLevel? level, AlertStatus? status, int count)
    {
        var page = await _alertQuery.PageAsync(level, status, count);
        return page.Items;
    }

    public Task<int> CountPendingAsync() => _alertQuery.CountPendingAsync();

    public async Task<bool> SetStatusAsync(long alertId, AlertStatus status)
    {
        var alert = await _alerts.GetByIdAsync(alertId);
        if (alert is null) return false;

        var now = DateTime.Now;
        switch (status)
        {
            case AlertStatus.Processed:
                alert.StartProcessing(now);
                break;
            case AlertStatus.Confirmed:
                alert.MarkHandled(now);
                break;
            case AlertStatus.Closed:
                alert.Dismiss(now);
                break;
            default:
                return false;
        }

        await _alerts.UpdateAsync(alert);
        return true;
    }
}
