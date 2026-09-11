using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Audit;

namespace Station.Desktop.ViewModels;

public sealed record AuditLogRow(
    string TimeText,
    string Operator,
    string Type,
    string Target,
    string Detail,
    string ResultText,
    string ResultColor);

/// <summary>日志中心：最近审计/操作日志（登录、配置修改、擦除、导入导出等）。</summary>
public partial class LogsModuleViewModel : ObservableObject, IDisposable
{
    private readonly IAuditLogService _audit;

    public ObservableCollection<AuditLogRow> Logs { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _message = string.Empty;

    public LogsModuleViewModel(IAuditLogService audit)
    {
        _audit = audit;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var logs = await _audit.GetRecentAsync(200);
            Logs.Clear();
            foreach (var log in logs)
            {
                Logs.Add(new AuditLogRow(
                    log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    log.OperatorName ?? log.OperatorAccount ?? "system",
                    log.OperationType,
                    log.Target ?? string.Empty,
                    log.Detail ?? string.Empty,
                    log.Result == 1 ? "成功" : "失败",
                    log.Result == 1 ? "#22c55e" : "#ef4444"));
            }

            Message = logs.Count > 0 ? $"最近 {logs.Count} 条操作日志" : "暂无日志";
        }
        catch (Exception ex)
        {
            Message = $"加载日志失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
    }
}
