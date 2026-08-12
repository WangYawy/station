using Avalonia.Input;
using Avalonia.Controls;
using Station.Application.Authentication;
using Station.Desktop.Application.Session;

namespace Station.Desktop.UI.Services;

/// <summary>
/// 会话空闲追踪：必须登录模式下，无操作达到配置时长（默认 1 分钟）自动退出登录。
/// 键盘/鼠标任意输入都会重置计时。
/// </summary>
public sealed class SessionIdleTracker : IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly AuthOptions _options;
    private CancellationTokenSource? _cts;
    private TopLevel? _topLevel;

    public SessionIdleTracker(ISessionManager sessions, AuthOptions options)
    {
        _sessions = sessions;
        _options = options;
        _sessions.SessionChanged += OnSessionChanged;
    }

    public void Attach(TopLevel topLevel)
    {
        if (_topLevel is not null)
        {
            return;
        }

        _topLevel = topLevel;
        topLevel.PointerMoved += OnPointerActivity;
        topLevel.KeyDown += OnKeyActivity;
        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        if (_sessions.IsAuthenticated)
        {
            ResetTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void OnPointerActivity(object? sender, PointerEventArgs e)
    {
        if (_sessions.IsAuthenticated)
        {
            ResetTimer();
        }
    }

    private void OnKeyActivity(object? sender, KeyEventArgs e)
    {
        if (_sessions.IsAuthenticated)
        {
            ResetTimer();
        }
    }

    private void ResetTimer()
    {
        StopTimer();
        if (_options.AutoLogoutMinutes <= 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = RunIdleTimerAsync(_cts.Token);
    }

    private async Task RunIdleTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(_options.AutoLogoutMinutes), cancellationToken);
            _sessions.Clear();
        }
        catch (OperationCanceledException)
        {
            // 有操作，计时已重置
        }
    }

    private void StopTimer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        StopTimer();
        _sessions.SessionChanged -= OnSessionChanged;
        if (_topLevel is not null)
        {
            _topLevel.PointerMoved -= OnPointerActivity;
            _topLevel.KeyDown -= OnKeyActivity;
        }
    }
}
