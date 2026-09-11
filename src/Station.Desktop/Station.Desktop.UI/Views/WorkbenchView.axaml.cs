using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Station.Application.Collecting;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class WorkbenchView : UserControl
{
    public WorkbenchView()
    {
        InitializeComponent();
    }

    private async void OnCardDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UsbPortCardViewModel card } &&
            card.TaskId is { } taskId &&
            DataContext is WorkbenchViewModel viewModel)
        {
            e.Handled = true;
            IReadOnlyList<CollectFileDto> files;
            try
            {
                files = await viewModel.GetTaskFilesAsync(taskId);
            }
            catch
            {
                files = [];
            }

            var window = new TaskFilesWindow($"{card.PortText} · {card.DeviceText}", files);
            await Dispatcher.UIThread.InvokeAsync(() => window.Show(TopLevel.GetTopLevel(this) as Window));
        }
    }
}
