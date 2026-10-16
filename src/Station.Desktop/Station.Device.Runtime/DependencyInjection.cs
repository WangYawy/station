using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Device.Detection;
using Station.Device.Events;
using Station.Device.FileSystem;
using Station.Device.Identity;
using Station.Device.Port;
using Station.Device.Runtime.Detection;
using Station.Device.Runtime.FileSystem;
using Station.Device.Runtime.Identity;
using Station.Device.Runtime.Port;
using Station.Device.Options;

namespace Station.Device.Runtime;

/// <summary>Station.Device 层 DI 注册入口。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddStationDevice(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DeviceDetectionOptions>(
            configuration.GetSection(DeviceDetectionOptions.SectionName));
        services.Configure<DeviceTransportOptions>(
            configuration.GetSection(DeviceTransportOptions.SectionName));

        // 物理端口
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IUsbPortResolver, WindowsPortResolver>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<IUsbPortResolver, LinuxPortResolver>();

        // 身份
        services.AddSingleton<IDeviceIdentityResolver, DeviceIdentityResolver>();

        // 检测器
        services.AddSingleton<IDeviceDetector, UmsDetector>();
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IDeviceDetector, WpdMtpDetector>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<IDeviceDetector, LinuxMtpDetector>();

        // 文件系统
        services.AddSingleton<IDeviceFileSystem, UmsFileSystem>();
        services.AddSingleton<IDeviceFileSystem, SimulatedFileSystem>();
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IDeviceFileSystem, WpdMtpFileSystem>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<IDeviceFileSystem, LinuxMtpFileSystem>();

        services.AddSingleton<IDeviceFileSystemProvider, DefaultFileSystemProvider>();

        // 在线聚合 + HostedService
        services.AddSingleton<DevicePresenceService>();
        services.AddSingleton<IDevicePresenceService>(sp => sp.GetRequiredService<DevicePresenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<DevicePresenceService>());

        return services;
    }
}
