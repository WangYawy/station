using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Station.Desktop.Views;

public enum MessageKind { Info, Warning, Error }

public partial class MessageDialog : Window
{
    public MessageDialog() { InitializeComponent(); }

    public static Task ShowAsync(Window owner, string title, string message, MessageKind kind)
    {
        var dlg = new MessageDialog { Title = title };
        dlg.MessageText.Text = message;
        dlg.IconText.Text = kind switch
        {
            MessageKind.Info => "ℹ",
            MessageKind.Warning => "⚠",
            MessageKind.Error => "✖",
            _ => ""
        };
        dlg.IconText.Foreground = kind switch
        {
            MessageKind.Info => Brush.Parse("#2563eb"),
            MessageKind.Warning => Brush.Parse("#f59e0b"),
            MessageKind.Error => Brush.Parse("#dc2626"),
            _ => Brush.Parse("#64748b")
        };
        return dlg.ShowDialog(owner);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
