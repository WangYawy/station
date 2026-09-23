using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.IdGenerators;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using Station.Domain.Security;

namespace Station.Application.Alerts;

public sealed class AlertService : IAlertService
{
    private readonly IRepository<Alert> _alerts;
    private readonly IIdGenerator _idGenerator;
    private readonly ISyncOutboxService _outbox;
    private readonly ReportingOptions _reportingOptions;
    private readonly IStationContext _stationContext;
    private readonly ILogger<AlertService> _logger;
    private readonly IReportingSigner _reportSignature;

    public AlertService(
        IRepository<Alert> alerts,
        IIdGenerator idGenerator,
        ISyncOutboxService outbox,
        ReportingOptions reportingOptions,
        IStationContext stationContext,
        IReportingSigner reportSignature,
        ILogger<AlertService> logger)
    {
        _alerts = alerts;
        _idGenerator = idGenerator;
        _outbox = outbox;
        _reportingOptions = reportingOptions;
        _stationContext = stationContext;
        _logger = logger;
        _reportSignature = reportSignature;
    }

    public async Task WriteAsync(Alert alert)
    {
        alert.Id = _idGenerator.NextId();
        alert.CreatedAt = DateTime.Now;
        await _alerts.InsertAsync(alert);
        _logger.LogInformation("报警写入 {Type}/{Level}：{Title}（来源 {Source}）", alert.Type, alert.Level, alert.Title, alert.Source);

        // 报警自动上报平台（SM2 签名；平台未连接时进入 Outbox 断网补报）
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
                var (signature, _) = await _reportSignature.SignAsync(canonical);
                if (signature is not null)
                    report = report with { Signature = signature };
            }

            await _outbox.EnqueueAsync("alert", System.Text.Json.JsonSerializer.Serialize(report));
        }
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        AlertLevel? level,
        AlertStatus? status,
        int count)
    {
        var query = new PageQuery<Alert>
        {
            PageIndex = 1,
            PageSize = count,
            Predicate = a =>
                (level == null || a.Level == level) &&
                (status == null || a.Status == status),
            CountTotal = false
        };
        var page = await _alerts.ToPageAsync(query, orderBy: a => a.CreatedAt, descending: true);
        return page.Items.Select(ToDto).ToList();
    }

    public async Task<int> CountPendingAsync() =>
        await _alerts.CountAsync(a => a.Status == AlertStatus.Pending);

    public async Task<bool> SetStatusAsync(long alertId, AlertStatus status)
    {
        var alert = await _alerts.GetByIdAsync(alertId);
        if (alert is null)
        {
            return false;
        }

        alert.Status = status;
        await _alerts.UpdateAsync(alert);
        return true;
    }

    private static AlertDto ToDto(Alert a) => new(
        a.Id, a.Type, a.Level, a.Status, a.Title, a.Detail, a.Source, a.CreatedAt);
}
