namespace Station.Desktop.Services.Kiosk;
/// <summary>
/// Kiosk 增强：屏蔽系统级切屏快捷键。
/// 平台无关层只做"软"约束，具体"硬"约束由各平台实现。
/// </summary>
public interface IKioskGuard : IDisposable
{
    /// <summary>后门触发（连按 Shift 5 次 / 组合键等）</summary>
    event Action? RequestExit;

    /// <summary>是否成功启用了硬拦截</summary>
    bool IsHardened { get; }

    void Install();
    void Uninstall();
}
