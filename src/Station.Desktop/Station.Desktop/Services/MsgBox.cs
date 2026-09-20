
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Station.Desktop.Services;
public static class DialogHelper
{
    public static async Task<bool> ConfirmAsync(Avalonia.Controls.Window owner, string title, string message)
    {
        var result = false;
        var dlg = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = (IBrush?)Avalonia.Application.Current?.FindResource("SurfaceBackground"),
        };

        var ok = new Button { Content = "确认", MinWidth = 100, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 100, IsCancel = true };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
                                HorizontalAlignment = HorizontalAlignment.Center },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { ok, cancel }
                }
            }
        };

        ok.Click += (_, _) => { result = true; dlg.Close(); };
        cancel.Click += (_, _) => { result = false; dlg.Close(); };

        await dlg.ShowDialog(owner);
        return result;
    }
}
