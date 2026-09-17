namespace Station.Application.Settings;

/// <summary>
/// 窗口显示设置项
/// </summary>
public sealed class WindowModeOptions
{
    public const string SectionName = "WindowMode";

    /// <summary>Kiosk 模式：无边框全屏 + 置顶 + 隐藏任务栏图标</summary>
    public bool Kiosk { get; set; } = false;

    /// <summary>锁定切屏（屏蔽 Alt+Tab / Win / Alt+F4 / Esc）—— 需平台钩子，见 KioskGuard</summary>
    public bool LockShortcuts { get; set; } = false;

    /// <summary>退出程序是否需要二次确认</summary>
    public bool ConfirmOnExit { get; set; } = true;
    #region 强制退出

    /// <summary>紧急后门：按住 Shift 点击关闭按钮直接退出，绕过所有确认</summary>
    public bool ShiftEnabled { get; set; } = true;
    /// <summary>长按退出按钮触发 PIN 输入框的秒数</summary>
    public double LongPressSeconds { get; set; } = 3.0;

    /// <summary>退出后门 PIN（默认 "123456"，空字符串 = 禁用 PIN 后门）</summary>
    public string ExitPin { get; set; } = "123456";

    /// <summary>PIN 位数（默认 4，同时用于掩码显示和自动提交判断）</summary>
    public int ExitPinLength { get; set; } = 6;
    #endregion
    #region 采集卡片相关
    /// <summary>行数</summary>
    public int Rows { get; set; } = 4;
    /// <summary>每行列数</summary>
    public int Columns { get; set; } = 5;

    /// <summary>卡片宽度</summary>
    //public int CardWidth { get; set; } = 240;
    ///// <summary>卡片高度</summary>
    //public int CardHeight { get; set; } = 200;

    /// <summary>卡片最小宽度（px）</summary>
    public double MinCardWidth { get; set; } = 240;

    /// <summary>卡片最小高度（px）</summary>
    public double MinCardHeight { get; set; } = 80;
    #endregion
    #region 触屏模式
    /// <summary>触屏优先模式：全局控件最小热区放大、滚动条加宽、长按代替右键</summary>
    public bool TouchOptimized { get; set; } = true;

    /// <summary>触屏场景下按钮最小高度（px）</summary>
    public double TouchMinTargetHeight { get; set; } = 44;

    /// <summary>触屏场景下滚动条宽度（px）</summary>
    public double TouchScrollBarWidth { get; set; } = 14;
    #endregion

    /// <summary>未登录状态下退出程序，是否需要管理员身份验证</summary>
    public bool RequireAdminOnExitWhenAnonymous { get; set; } = true;

    /// <summary>退出验证的默认管理员账号（预填到输入框）</summary>
    public string DefaultAdminUsername { get; set; } = "admin";
}
