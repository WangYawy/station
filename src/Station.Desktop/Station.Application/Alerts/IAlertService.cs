using Station.Domain.Entities;
using Station.Contracts;

namespace Station.Application.Alerts;

public sealed record AlertDto(
    long Id,
    AlertType Type,
    AlertLevel Level,
    AlertStatus Status,
    string Title,
    string? Detail,
    string? Source,
    DateTime CreatedAt);

/// <summary>报警服务：写入、查询筛选、状态流转（确认/处理/关闭）。</summary>
public interface IAlertService
{
    Task WriteAsync(Alert alert);

    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(AlertLevel? level, AlertStatus? status, int count);

    Task<int> CountPendingAsync();

    Task<bool> SetStatusAsync(long alertId, AlertStatus status);
}
