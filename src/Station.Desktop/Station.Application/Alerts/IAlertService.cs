using Station.Contracts;
using Station.Application.Alerts.Dtos;

namespace Station.Application.Alerts;

/// <summary>报警服务：写入、查询筛选、状态流转（确认/处理/关闭）。</summary>
public interface IAlertService
{
    Task WriteAsync(AlertType type, AlertLevel level, string title,
        string? detail = null, string? source = null, long? deptId = null);

    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(AlertLevel? level, AlertStatus? status, int count);

    Task<int> CountPendingAsync();

    Task<bool> SetStatusAsync(long alertId, AlertStatus status);
}
