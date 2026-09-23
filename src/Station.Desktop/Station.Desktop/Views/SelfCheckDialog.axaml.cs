using Avalonia.Controls;
using Avalonia.Interactivity;
using Station.Application.Diagnostics;

namespace Station.Desktop.Views;

/// <summary>
/// 启动自检失败时的告警对话框。
/// 用户可选"退出"（返回 false）或"继续启动"（返回 true）。
/// </summary>
public partial class SelfCheckDialog : Window
{
    public SelfCheckDialog() { InitializeComponent(); }

    public SelfCheckDialog(SelfCheckReport report) : this()
    {
        DataContext = report;
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close(false);
}
