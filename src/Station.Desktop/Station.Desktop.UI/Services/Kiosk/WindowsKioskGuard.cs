using System.Runtime.InteropServices;

namespace Station.Desktop.Services.Kiosk;

internal sealed class WindowsKioskGuard : IKioskGuard
{
    public event Action? RequestExit;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private int _shiftHits;
    private DateTime _lastShift = DateTime.MinValue;

    public bool IsHardened => _hookId != IntPtr.Zero;

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        _hookId = NativeMethods.SetHook(_proc);
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN ||
                           wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam);
            var alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
            var win = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                       (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
            var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;

            if ((alt && vk == NativeMethods.VK_TAB) ||
                (alt && vk == NativeMethods.VK_F4) ||
                win ||
                (ctrl && vk == NativeMethods.VK_ESC))
                return (IntPtr)1;

            if (vk is NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastShift).TotalMilliseconds > 500) _shiftHits = 0;
                _lastShift = now;
                if (++_shiftHits >= 5) { _shiftHits = 0; RequestExit?.Invoke(); }
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static class NativeMethods
    {
        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int VK_TAB = 0x09, VK_ESC = 0x1B, VK_F4 = 0x73;
        internal const int VK_MENU = 0x12, VK_CONTROL = 0x11;
        internal const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        internal const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

        internal static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using var cur = System.Diagnostics.Process.GetCurrentProcess();
            using var mod = cur.MainModule!;
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(mod?.ModuleName), 0);
        }
    }
}
