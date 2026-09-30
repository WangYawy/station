using Avalonia.Controls;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class TaskFilesWindow : Window
{
    public TaskFilesWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is TaskFilesViewModel vm)
        {
            vm.Dispose();
            DataContext = null;
        }
    }
}
