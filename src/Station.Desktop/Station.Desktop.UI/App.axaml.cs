using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Desktop.Bootstrapper;
using Station.Desktop.UI.Views;

namespace Station.Desktop.UI;

public partial class App : Avalonia.Application
{
    private IHost? _host;

    /// <summary>应用级服务容器（Host 构建后可用，供视图/服务解析依赖）。</summary>
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = HostBuilderFactory.Create().Build();
            _host.StartAsync().GetAwaiter().GetResult();
            Services = _host.Services;

            desktop.MainWindow = new ShellWindow();

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (_host is not null)
                {
                    await _host.StopAsync();
                    _host.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
