using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Desktop.Application.Session;

namespace Station.Desktop.UI.ViewModels;

public sealed record CollectTaskItemViewModel(
    long TaskId,
    string TaskNo,
    string RecorderName,
    string StatusText,
    string StatusColor,
    string ProgressText,
    string SpeedText,
    string IsAutoText,
    bool IsActive);

public sealed record CollectFileItemViewModel(
    long FileId,
    string FileName,
    string SizeText,
    string StatusText,
    string StatusColor,
    double Progress,
    string SpeedText);

public partial class CollectModuleViewModel : ObservableObject, IDisposable
{
    private readonly ICollectTaskService _service;
    private readonly ISessionManager _sessions;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<CollectTaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<CollectFileItemViewModel> Files { get; } = [];

    [ObservableProperty]
    private CollectTaskItemViewModel? _selectedTask;

    [ObservableProperty]
    private string? _message;

    public CollectModuleViewModel(ICollectTaskService service, ISessionManager sessions)
    {
        _service = service;
        _sessions = sessions;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
    }

    partial void OnSelectedTaskChanged(CollectTaskItemViewModel? value)
    {
        _ = RefreshFilesAsync(value?.TaskId);
    }

    [RelayCommand]
    private async Task SimulateConnectAsync()
    {
        var now = DateTime.Now;
        var device = new CollectDeviceInfo(
            $"记录仪-{now:mmss}",
            $"SIM-{now:HHmmss}",
            ProtocolType.Ums);
        var task = await _service.CreateTaskAsync(device, isAuto: true);
        Message = $"已模拟接入 {task.RecorderName}，自动采集已启动";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _service.StartAsync(SelectedTask.TaskId);
        Message = $"任务 {SelectedTask.TaskNo} 开始采集";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _service.PauseAsync(SelectedTask.TaskId);
        Message = $"任务 {SelectedTask.TaskNo} 已暂停";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _service.ResumeAsync(SelectedTask.TaskId);
        Message = $"任务 {SelectedTask.TaskNo} 已恢复";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _service.CancelAsync(SelectedTask.TaskId);
        Message = $"任务 {SelectedTask.TaskNo} 已取消";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SimulateDisconnectAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _service.InterruptAsync(SelectedTask.TaskId, "模拟拔出：设备断开");
        Message = $"任务 {SelectedTask.TaskNo} 已中断（设备断开）";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var tasks = await _service.GetTasksAsync(30);
            var selectedId = SelectedTask?.TaskId;
            Tasks.Clear();
            foreach (var task in tasks)
            {
                Tasks.Add(new CollectTaskItemViewModel(
                    task.TaskId,
                    task.TaskNo,
                    task.RecorderName,
                    CollectTaskStatusText.Of(task.Status),
                    StatusColor(task.Status),
                    $"{task.CollectedFiles}/{task.TotalFiles}",
                    FormatSpeed(task.SpeedBytesPerSecond),
                    task.IsAuto ? "自动" : "手动",
                    task.Status is Station.Domain.Enums.CollectTaskStatus.Scanning
                        or Station.Domain.Enums.CollectTaskStatus.Collecting
                        or Station.Domain.Enums.CollectTaskStatus.Paused));
            }

            SelectedTask = Tasks.FirstOrDefault(t => t.TaskId == selectedId) ?? Tasks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Message = $"刷新失败：{ex.Message}";
        }
    }

    private async Task RefreshFilesAsync(long? taskId)
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
                Files.Add(new CollectFileItemViewModel(
                    file.FileId,
                    file.FileName,
                    FormatSize(file.Size),
                    CollectFileStatusText.Of(file.Status),
                    StatusColor(file.Status),
                    file.Progress * 100,
                    FormatSpeed(file.SpeedBytesPerSecond)));
            }
        }
        catch
        {
            // 任务可能已被清理，忽略
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private static string StatusColor(Station.Domain.Enums.CollectTaskStatus status) => status switch
    {
        Station.Domain.Enums.CollectTaskStatus.Completed => "#22c55e",
        Station.Domain.Enums.CollectTaskStatus.Collecting or Station.Domain.Enums.CollectTaskStatus.Scanning => "#2563eb",
        Station.Domain.Enums.CollectTaskStatus.Paused => "#f59e0b",
        Station.Domain.Enums.CollectTaskStatus.Interrupted or Station.Domain.Enums.CollectTaskStatus.Failed => "#ef4444",
        _ => "#94a3b8"
    };

    private static string StatusColor(Station.Domain.Enums.CollectFileStatus status) => status switch
    {
        Station.Domain.Enums.CollectFileStatus.Completed => "#22c55e",
        Station.Domain.Enums.CollectFileStatus.Copying or Station.Domain.Enums.CollectFileStatus.Verifying => "#2563eb",
        Station.Domain.Enums.CollectFileStatus.Skipped => "#94a3b8",
        Station.Domain.Enums.CollectFileStatus.Failed or Station.Domain.Enums.CollectFileStatus.Abnormal => "#ef4444",
        _ => "#94a3b8"
    };

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? "--" : $"{bytesPerSecond / 1024 / 1024:F1} MB/s";

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:F1} MB" : $"{bytes / 1024d:F0} KB";
}
