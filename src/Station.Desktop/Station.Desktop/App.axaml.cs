using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Diagnostics;
using Station.Application.Settings;
using Station.Application.Storage;
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
        // 全局应用中文
        var zh = new CultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = zh;
        CultureInfo.DefaultThreadCurrentUICulture = zh;
        Thread.CurrentThread.CurrentCulture = zh;
        Thread.CurrentThread.CurrentUICulture = zh;

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // ---- 1. 主机 ----
                _host = HostBuilderFactory.Create().Build();
                _host.Start();
                Services = _host.Services;
                // 在创建任何窗口之前注入配置到资源字典
                ApplyWindowModeOptions();

                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose; // 随主窗口关闭而关闭，不被子窗口拖住

                // ---- 2. 创建主窗口 ----
                var shell = new ShellWindow();
                desktop.MainWindow = shell;

                // ---- 3. 关停清理（同步，确保 Host 真的停掉）----
                desktop.ShutdownRequested += async (_, _) =>
                {
                    if (_host is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _host.StopAsync(cts.Token);
                        _host.Dispose();
                    }
                };

                // ---- 4. 异步启动流程：等主窗口真正可见后再跑 ----
                shell.Opened += OnShellOpened;
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            // 兜底
            // await ShowFatalErrorAsync(ex);
            throw;
        }
    }

    private async void OnShellOpened(object? sender, EventArgs e)
    {
        var shell = (ShellWindow)sender!;
        shell.Opened -= OnShellOpened;          // 只跑一次

        try
        {
            await RunStartupFlowAsync(shell);
        }
        catch (Exception ex)
        {
            // TODO: 记录日志；可选择提示后继续运行，而不是直接崩
        }
    }

    private async Task RunStartupFlowAsync(ShellWindow shell)
    {
        // seed
        await Services!.GetRequiredService<IStorageConfigStore>().SeedIfEmptyAsync();

        // 自检
        var report = await Services.GetRequiredService<IStartupSelfCheckService>().RunAsync();
        if (report.IsHealthy) return;

        // 关键：走 ShellWindow 的模态通道（_modalDepth 保护 + owner 已可见）
        var dialog = new SelfCheckDialog(report);
        var canContinue = await shell.ShowModalAsync(dialog, () => dialog.ShowDialog<bool>(shell));

        if (!canContinue && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();     // 触发 Exit → StopHost()
    }

    private void StopHost()
    {
        var host = Interlocked.Exchange(ref _host, null);
        if (host is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            host.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch { /* TODO: 记录日志 */ }
        finally { host.Dispose(); }
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
