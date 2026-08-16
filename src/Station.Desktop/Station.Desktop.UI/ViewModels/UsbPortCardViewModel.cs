using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting;
using Station.Domain.Enums;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 工作台 30 路 USB 采集通道卡片：实时反映该端口当前任务（采集中/已暂停/空闲），
/// 提供暂停/恢复/取消/重试/查看明细操作。
/// </summary>
public partial class UsbPortCardViewModel : ObservableObject
{
    public string PortText { get; }

    [ObservableProperty]
    private long? _taskId;

    [ObservableProperty]
    private string _taskNo = string.Empty;

    [ObservableProperty]
    private string _statusText = "空闲";

    [ObservableProperty]
    private string _statusBadgeBrush = "#f1f5f9";

    [ObservableProperty]
    private string _statusForeground = "#94a3b8";

    [ObservableProperty]
    private string _deviceText = "-- 等待设备连接";

    [ObservableProperty]
    private string _metaText = "--";

    [ObservableProperty]
    private string _storageText = "--";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "0%";

    [ObservableProperty]
    private string _speedText = "-- MB/s";

    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private double _opacity = 0.55;

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canRetry;

    public IRelayCommand PauseCommand { get; }

    public IRelayCommand ResumeCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand RetryCommand { get; }

    public UsbPortCardViewModel(
        int portIndex,
        Action<UsbPortCardViewModel> pause,
        Action<UsbPortCardViewModel> resume,
        Action<UsbPortCardViewModel> cancel,
        Action<UsbPortCardViewModel> retry)
    {
        PortText = $"#{portIndex:D2}";
        PauseCommand = new RelayCommand(() => pause(this), () => CanPause);
        ResumeCommand = new RelayCommand(() => resume(this), () => CanResume);
        CancelCommand = new RelayCommand(() => cancel(this), () => CanCancel);
        RetryCommand = new RelayCommand(() => retry(this), () => CanRetry);
    }

    public void SetIdle()
    {
        TaskId = null;
        TaskNo = string.Empty;
        StatusText = "空闲";
        StatusBadgeBrush = "#f1f5f9";
        StatusForeground = "#94a3b8";
        DeviceText = "-- 等待设备连接";
        MetaText = "--";
        StorageText = "--";
        Progress = 0;
        ProgressText = "0%";
        SpeedText = "-- MB/s";
        IsIdle = true;
        Opacity = 0.55;
        CanPause = CanResume = CanCancel = CanRetry = false;
        NotifyCommands();
    }

    public void UpdateFromTask(CollectTaskDto task)
    {
        TaskId = task.TaskId;
        TaskNo = task.TaskNo;
        IsIdle = false;
        Opacity = 1;
        DeviceText = task.RecorderName;
        MetaText = $"{ProtocolText(task)} · {(task.IsAuto ? "自动采集" : "手动采集")}";
        StorageText = $"文件 {task.CollectedFiles}/{task.TotalFiles} · 已采集 {FormatSize(task.CollectedBytes)}";

        var percent = task.TotalFiles > 0
            ? (double)task.CollectedFiles / task.TotalFiles
            : task.TotalBytes > 0
                ? (double)task.CollectedBytes / task.TotalBytes
                : 0d;
        Progress = Math.Clamp(percent * 100, 0, 100);
        ProgressText = $"{Progress:F0}%";
        SpeedText = task.SpeedBytesPerSecond > 0
            ? $"{task.SpeedBytesPerSecond / 1024 / 1024:F1} MB/s"
            : "-- MB/s";

        switch (task.Status)
        {
            case CollectTaskStatus.Scanning:
            case CollectTaskStatus.Collecting:
                StatusText = "采集中";
                StatusBadgeBrush = "#dbeafe";
                StatusForeground = "#1d4ed8";
                CanPause = true;
                CanCancel = true;
                break;
            case CollectTaskStatus.Paused:
                StatusText = "已暂停";
                StatusBadgeBrush = "#fef3c7";
                StatusForeground = "#b45309";
                CanResume = true;
                CanCancel = true;
                break;
            case CollectTaskStatus.Failed:
                StatusText = "故障";
                StatusBadgeBrush = "#fee2e2";
                StatusForeground = "#b91c1c";
                CanRetry = true;
                break;
            case CollectTaskStatus.Completed:
                StatusText = "已完成";
                StatusBadgeBrush = "#dcfce7";
                StatusForeground = "#15803d";
                break;
            default:
                StatusText = "待开始";
                StatusBadgeBrush = "#f1f5f9";
                StatusForeground = "#64748b";
                break;
        }

        NotifyCommands();
    }

    private void NotifyCommands()
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private static string ProtocolText(CollectTaskDto task) => task.Protocol switch
    {
        Station.Contracts.ProtocolType.Mtp => "MTP",
        Station.Contracts.ProtocolType.PrivateSdk => "私有SDK",
        _ => "UMS"
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
}
