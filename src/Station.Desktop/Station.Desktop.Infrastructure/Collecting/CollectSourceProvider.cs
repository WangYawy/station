using Station.Application.Collecting;
using Station.Contracts;
using Station.Infrastructure.Collecting;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 采集源路由：真实模式（SourceMode=ums/mtp）下按设备协议选择——
/// UMS/私有SDK(转U盘) → UmsCollectSource，MTP → MtpCollectSource（Windows）/LinuxMtpCollectSource。
/// </summary>
public sealed class CollectSourceProvider : ICollectSourceProvider
{
    private readonly CollectOptions _options;
    private readonly UmsCollectSource _ums;
    private readonly MtpCollectSource? _mtp;
    private readonly LinuxMtpCollectSource? _mtpLinux;
    private readonly SimulatedCollectSource _simulated;

    public CollectSourceProvider(
        CollectOptions options,
        UmsCollectSource ums,
        SimulatedCollectSource simulated,
        MtpCollectSource? mtp = null,
        LinuxMtpCollectSource? mtpLinux = null)
    {
        _options = options;
        _ums = ums;
        _simulated = simulated;
        _mtp = mtp;
        _mtpLinux = mtpLinux;
    }

    public ICollectSource GetFor(ProtocolType protocol)
    {
        if (_options.SourceMode == "simulated")
        {
            return _simulated;
        }

        return protocol switch
        {
            ProtocolType.Mtp => OperatingSystem.IsWindows()
                ? _mtp ?? throw new PlatformNotSupportedException("未适配 MTP 采集源")
                : OperatingSystem.IsLinux()
                    ? _mtpLinux ?? throw new PlatformNotSupportedException("未安装 libmtp")
                    : throw new PlatformNotSupportedException("当前平台不支持 MTP 采集源"),
            _ => _ums // Ums / PrivateSdk（私有加密 SDK 转 U 盘模式）
        };
        
    }
}
