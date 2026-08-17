using Station.Application.Collecting;
using Station.Contracts;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 桌面端采集源路由：真实模式（SourceMode=ums/mtp）下按设备协议选择——
/// UMS/私有SDK(转U盘) → UmsCollectSource，MTP → MtpCollectSource（仅 Windows）。
/// </summary>
public sealed class CollectSourceProvider : ICollectSourceProvider
{
    private readonly CollectOptions _options;
    private readonly UmsCollectSource _ums;
    private readonly MtpCollectSource? _mtp;
    private readonly SimulatedCollectSource _simulated;

    public CollectSourceProvider(
        CollectOptions options,
        UmsCollectSource ums,
        SimulatedCollectSource simulated,
        MtpCollectSource? mtp = null)
    {
        _options = options;
        _ums = ums;
        _simulated = simulated;
        _mtp = mtp;
    }

    public ICollectSource GetFor(ProtocolType protocol)
    {
        if (_options.SourceMode == "simulated")
        {
            return _simulated;
        }

        return protocol switch
        {
            ProtocolType.Mtp => _mtp ?? throw new PlatformNotSupportedException("MTP 采集源仅支持 Windows"),
            _ => _ums // Ums / PrivateSdk（私有加密 SDK 转 U 盘模式）
        };
    }
}
