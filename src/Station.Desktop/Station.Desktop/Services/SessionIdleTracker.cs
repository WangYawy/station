using Avalonia.Controls;
using Avalonia.Input;
using Station.Application.Authentication;
using Station.Application.Session;

namespace Station.Desktop.Services;

/// <summary>
/// 会话空闲追踪：无操作达到配置时长（默认 1 分钟）自动退出登录；
/// 临近超时（默认 10 秒）通过 <see cref="WarningChanged"/> 通知 UI 展示
/// "即将自动退出登录，请操作以保持会话"；任意输入重置计时。
/// </summary>
public sealed class SessionIdleTracker : IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _warningThreshold = TimeSpan.FromSeconds(10);
    private CancellationTokenSource? _cts;
    private TopLevel? _topLevel;

    /// <summary>剩余秒数；-1 表示隐藏警告。</summary>
    public event Action<int>? WarningChanged;

    public SessionIdleTracker(ISessionManager sessions, AuthOptions options)
    {
        _sessions = sessions;
        _timeout = TimeSpan.FromMinutes(options.AutoLogoutMinutes);
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
            WarningChanged?.Invoke(-1);
            StopTimer();
        }
    }

    private void OnPointerActivity(object? sender, PointerEventArgs e)
    {
        if (_sessions.IsAuthenticated)
        {
            WarningChanged?.Invoke(-1);
            ResetTimer();
        }
    }

    private void OnKeyActivity(object? sender, KeyEventArgs e)
    {
        if (_sessions.IsAuthenticated)
        {
            WarningChanged?.Invoke(-1);
            ResetTimer();
        }
    }

    private void ResetTimer()
    {
        StopTimer();
        if (_timeout <= TimeSpan.Zero)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = RunIdleLoopAsync(_cts.Token);
    }

    private async Task RunIdleLoopAsync(CancellationToken cancellationToken)
    {
        var remaining = (int)_timeout.TotalSeconds;
        try
        {
            while (remaining > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                remaining--;
                if (remaining <= (int)_warningThreshold.TotalSeconds)
                {
                    WarningChanged?.Invoke(remaining);
                }
            }

            WarningChanged?.Invoke(-1);
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
