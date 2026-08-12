using Station.Domain.Entities;

namespace Station.Application.Alerts;

/// <summary>报警服务（界面列表 P0；声音/弹窗后续接入）。</summary>
public interface IAlertService
{
    Task WriteAsync(Alert alert);
}
