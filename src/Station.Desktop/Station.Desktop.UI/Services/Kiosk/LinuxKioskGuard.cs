using System.Runtime.InteropServices;

namespace Station.Desktop.Services.Kiosk;

/// <summary>
/// Linux Kiosk：X11 下用 XGrabKeyboard 独占键盘；Wayland 合成器出于安全策略通常
/// 不允许全局抓取，此时降级为 Avalonia 层的软拉回（ShellWindow 负责）。
/// 麒麟 / 统信 UOS 大多基于 X11，此实现即可覆盖。
/// </summary>
internal sealed class LinuxKioskGuard : IKioskGuard
{
    public event Action? RequestExit;

    private IntPtr _display = IntPtr.Zero;
    private bool _grabbed;
    private int _shiftHits;
    private DateTime _lastShift = DateTime.MinValue;

    public bool IsHardened => _grabbed;

    public void Install()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLowerInvariant();
        if (session == "wayland") return;   // 交给 Avalonia 层

        try
        {
            _display = X11.XOpenDisplay(null);
            if (_display == IntPtr.Zero) return;

            // 只抓键盘（grab_mode = GrabModeAsync, owner_events = False）
            X11.XGrabKeyboard(_display, X11.DefaultRootWindow(_display),
                false, X11.GrabModeAsync, X11.GrabModeAsync, 0);
            _grabbed = true;

            // 后台线程轮询 Shift 后门（X11 无全局键盘事件回调，改用轮询）
            Task.Run(PollShiftAsync);
        }
        catch { /* 静默降级 */ }
    }

    private async Task PollShiftAsync()
    {
        var keyCode = (int)X11.XKeysymToKeycode(_display, X11.XK_Shift_L);
        var keymap = new byte[32];

        while (_grabbed && _display != IntPtr.Zero)
        {
            try
            {
                if (X11.XQueryKeymap(_display, keymap) != 0)
                {
                    var pressed = (keymap[keyCode / 8] & (1 << (keyCode % 8))) != 0;
                    if (pressed)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastShift).TotalMilliseconds > 500) _shiftHits = 0;
                        _lastShift = now;
                        if (++_shiftHits >= 5) { _shiftHits = 0; RequestExit?.Invoke(); }
                    }
                }
            }
            catch { }
            await Task.Delay(500);
        }
    }

    public void Uninstall()
    {
        if (_grabbed && _display != IntPtr.Zero)
        {
            try { X11.XUngrabKeyboard(_display, 0); } catch { }
            try { X11.XCloseDisplay(_display); } catch { }
        }
        _grabbed = false;
        _display = IntPtr.Zero;
    }

    public void Dispose() => Uninstall();

    private static class X11
    {
        internal const int GrabModeAsync = 1;
        internal const ulong XK_Shift_L = 0xFFE1;

        [DllImport("libX11.so.6")]
        internal static extern IntPtr XOpenDisplay(string? displayName);

        [DllImport("libX11.so.6")]
        internal static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        internal static extern IntPtr XDefaultRootWindow(IntPtr display);
        internal static IntPtr DefaultRootWindow(IntPtr d) => XDefaultRootWindow(d);

        [DllImport("libX11.so.6")]
        internal static extern int XGrabKeyboard(IntPtr display, IntPtr grabWindow, bool ownerEvents,
            int pointerMode, int keyboardMode, ulong time);

        [DllImport("libX11.so.6")]
        internal static extern int XUngrabKeyboard(IntPtr display, ulong time);

        [DllImport("libX11.so.6")]
        internal static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport("libX11.so.6")]
        internal static extern int XQueryKeymap(IntPtr display, byte[] keys);
    }
}
