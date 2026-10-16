using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Domain.Collecting;

namespace Station.Desktop.ViewModels;

public partial class CollectFileRowViewModel : ObservableObject
{
    public long FileId { get; }
    public string FileName { get; }
    public string SizeText { get; }

    [ObservableProperty] private CollectFileStatus _status;
    [ObservableProperty] private string _statusText = "--";
    [ObservableProperty] private string _statusBrush = "#94a3b8";

    public CollectFileRowViewModel(CollectFileDto dto)
    {
        FileId = dto.Id;
        FileName = dto.FileName;
        SizeText = FormatSize(dto.Size);
        ApplyStatus(dto.Status);
    }

    public void UpdateStatus(CollectFileStatus status) => ApplyStatus(status);

    private void ApplyStatus(CollectFileStatus s)
    {
        Status = s;
        (StatusText, StatusBrush) = s switch
        {
            CollectFileStatus.Pending => ("待采集", "#64748b"),
            CollectFileStatus.Copying => ("采集中", "#1d4ed8"),
            CollectFileStatus.Verifying => ("校验中", "#0891b2"),
            CollectFileStatus.Completed => ("已完成", "#15803d"),
            CollectFileStatus.Skipped => ("已跳过", "#7c3aed"),
            CollectFileStatus.Failed => ("失败", "#b91c1c"),
            CollectFileStatus.Abnormal => ("异常", "#b45309"),
            CollectFileStatus.Canceled => ("已取消", "#475569"),
            _ => ("--", "#94a3b8")
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:F1} {units[unit]}";
    }
}
