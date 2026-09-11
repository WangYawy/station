using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Domain.Enums;

namespace Station.Desktop.ViewModels;

public sealed record HistoryTaskRow(
    long TaskId,
    string TaskNo,
    string RecorderName,
    string StatusText,
    string StatusColor,
    string TimeText,
    string Summary);

public sealed record HistoryFileRow(
    string FileName,
    string SizeText,
    string StatusText,
    string TimeText,
    string? Error);

/// <summary>历史记录：采集任务列表 + 选中任务的文件明细。</summary>
public partial class HistoryModuleViewModel : ObservableObject, IDisposable
{
    private readonly ICollectTaskService _service;

    public ObservableCollection<HistoryTaskRow> Tasks { get; } = [];

    public ObservableCollection<HistoryFileRow> Files { get; } = [];

    [ObservableProperty]
    private HistoryTaskRow? _selectedTask;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _message = string.Empty;

    public HistoryModuleViewModel(ICollectTaskService service)
    {
        _service = service;
        _ = LoadAsync();
    }

    partial void OnSelectedTaskChanged(HistoryTaskRow? value)
    {
        _ = LoadFilesAsync(value?.TaskId);
    }

    private async Task LoadAsync()
    {
        try
        {
            var tasks = await _service.GetTasksAsync(200);
            Tasks.Clear();
            foreach (var task in tasks)
            {
                Tasks.Add(new HistoryTaskRow(
                    task.TaskId,
                    task.TaskNo,
                    task.RecorderName,
                    CollectTaskStatusText.Of(task.Status),
                    StatusColor(task.Status),
                    (task.CompletedAt ?? task.StartedAt)?.ToString("yyyy-MM-dd HH:mm:ss") ?? task.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    $"{task.CollectedFiles}/{task.TotalFiles} 个 · {FormatSize(task.TotalBytes)}"));
            }

            SelectedTask = Tasks.FirstOrDefault();
            if (Tasks.Count > 0)
            {
                Message = $"共 {Tasks.Count} 条任务记录";
            }
            else
            {
                Message = "暂无采集任务记录";
            }
        }
        catch (Exception ex)
        {
            Message = $"加载历史任务失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFilesAsync(long? taskId)
    {
        if (taskId is null)
        {
            Files.Clear();
            return;
        }

        try
        {
            var files = await _service.GetTaskFilesAsync(taskId.Value);
            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(new HistoryFileRow(
                    file.FileName,
                    FormatSize(file.Size),
                    CollectFileStatusText.Of(file.Status),
                    file.CollectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    file.ErrorMessage));
            }
        }
        catch (Exception ex)
        {
            Files.Clear();
            Message = $"加载任务文件失败：{ex.Message}";
        }
    }

    private static string StatusColor(CollectTaskStatus status) => status switch
    {
        CollectTaskStatus.Collecting or CollectTaskStatus.Scanning => "#2563eb",
        CollectTaskStatus.Completed => "#22c55e",
        CollectTaskStatus.Paused => "#f59e0b",
        CollectTaskStatus.Interrupted or CollectTaskStatus.Failed or CollectTaskStatus.Canceled => "#ef4444",
        _ => "#94a3b8"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }

    public void Dispose()
    {
    }
}
