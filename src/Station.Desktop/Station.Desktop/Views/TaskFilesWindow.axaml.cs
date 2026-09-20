using Avalonia.Controls;
using Station.Application.Collecting;

namespace Station.Desktop.Views;

public partial class TaskFilesWindow : Window
{
    public TaskFilesWindow(string title, IReadOnlyList<CollectFileDto> files, Window? owner = null)
    {
        InitializeComponent();
        if (owner is not null)
        {
            Owner = owner;
        }

        TitleText.Text = $"📁 {title}";
        FileList.ItemsSource = files;
        SummaryText.Text = $"共 {files.Count} 个文件";
    }
}
