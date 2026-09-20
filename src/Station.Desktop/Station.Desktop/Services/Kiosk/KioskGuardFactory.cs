
namespace Station.Desktop.Services.Kiosk;
public static class KioskGuardFactory
{
    /// <summary>按平台创建实现（不通过容器）</summary>
    public static IKioskGuard Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsKioskGuard();
        if (OperatingSystem.IsLinux()) return new LinuxKioskGuard();
        return new NoopKioskGuard();
    }
}
