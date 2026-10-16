using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting.Dtos;
using Station.Application.Notifications;
using Station.Domain.Collecting;

namespace Station.Desktop.ViewModels;

public partial class TaskFilesViewModel : ObservableObject, IDisposable
{
    private readonly long _taskId;
    private readonly IUiNotificationService _events;

    public string Title { get; }
    public ObservableCollection<CollectFileRowViewModel> Files { get; }

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private string _summaryText = "";

    public TaskFilesViewModel(
        long taskId,
        string title,
        IReadOnlyList<CollectFileDto> files,
        IUiNotificationService events)
    {
        _taskId = taskId;
        _events = events;
        Title = title;

        Files = new ObservableCollection<CollectFileRowViewModel>(
            files.Select(f => new CollectFileRowViewModel(f)));

        TotalCount = Files.Count;
        CompletedCount = Files.Count(f => f.Status == CollectFileStatus.Completed);
        UpdateSummary();

        _events.TaskFileCompleted += OnFileCompleted;
    }

    private void OnFileCompleted(object? sender, TaskFileCompletedEvent e)
    {
        if (e.TaskId != _taskId) return;

        if (Dispatcher.UIThread.CheckAccess())
            Apply(e);
        else
            Dispatcher.UIThread.Post(() => Apply(e), DispatcherPriority.Normal);
    }

    private void Apply(TaskFileCompletedEvent e)
    {
        var row = Files.FirstOrDefault(r => r.FileId == e.FileId);
        if (row is null) return;

        var wasCompleted = row.Status == CollectFileStatus.Completed;
        row.UpdateStatus(e.Status);
        var nowCompleted = e.Status == CollectFileStatus.Completed;

        if (!wasCompleted && nowCompleted) CompletedCount++;
        else if (wasCompleted && !nowCompleted) CompletedCount--;

        UpdateSummary();
    }

    private void UpdateSummary()
        => SummaryText = $"已完成 {CompletedCount} / {TotalCount}";

    public void Dispose() => _events.TaskFileCompleted -= OnFileCompleted;
}
