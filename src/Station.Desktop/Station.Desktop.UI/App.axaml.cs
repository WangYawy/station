using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Settings;
using Station.Desktop.Bootstrapper;
using Station.Desktop.Views;

namespace Station.Desktop;

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
        // 在创建任何窗口之前注入配置到资源字典
        ApplyWindowModeOptions();

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

    /// <summary>
    /// 把 WindowModeOptions 里的触屏相关配置注入到 Application.Resources，
    /// 让 {DynamicResource TouchTargetMinHeight} / {DynamicResource TouchScrollBarWidth}
    /// 自动解析到配置值。支持运行时再次调用实现热更新。
    /// </summary>
    public static void ApplyWindowModeOptions()
    {
        if (Current is null) return;
        
        var options = Services?
            .GetService<IOptions<WindowModeOptions>>()?.Value
            ?? new WindowModeOptions();   // 兜底

        var res = Current.Resources;

        if (options.TouchOptimized)
        {
            // 触屏模式：使用配置值（允许按屏幕尺寸微调）
            res["TouchTargetMinHeight"] = options.TouchMinTargetHeight;
            res["TouchScrollBarWidth"] = options.TouchScrollBarWidth;
            res["ShellCardMinHeight"] = options.MinCardHeight;
        }
        else
        {
            // 键鼠模式：使用更紧凑的默认值
            res["TouchTargetMinHeight"] = 32d;
            res["TouchScrollBarWidth"] = 8d;
            res["ShellCardMinHeight"] = options.MinCardHeight;
        }
    }

    ///// <summary>
    ///// 运行时热更新，设置页面允许用户改这些值
    ///// </summary>
    //public static void ReloadTouchOptions()
    //{
    //    ApplyWindowModeOptions();   // 重新读配置 → 覆盖资源
    //}

    //// 设置页面调用 会自动刷新，无需重启应用
    //public void OnTouchOptionsChanged()
    //{
    //    App.ApplyWindowModeOptions();
    //}
}
