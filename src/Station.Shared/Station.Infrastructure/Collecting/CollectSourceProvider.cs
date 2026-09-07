using Station.Application.Collecting;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 采集源路由：真实模式（SourceMode=ums/mtp）下按设备协议选择——
/// UMS/私有SDK(转U盘) → UmsCollectSource，MTP → MtpCollectSource（Windows）/LinuxMtpCollectSource。
/// </summary>
public sealed class CollectSourceProvider : ICollectSourceProvider
{
    private readonly IReadOnlyDictionary<ProtocolType, ICollectSource> _sources;

    // 构造函数显式注入所有需要的具体实现
    public CollectSourceProvider(
        UmsCollectSource umsSource,
        SimulatedCollectSource simulatedSource,
        MtpCollectSource? mtpSource = null,      // Windows 下注入
        LinuxMtpCollectSource? linuxMtpSource = null) // Linux 下注入
    {
        var dict = new Dictionary<ProtocolType, ICollectSource>
        {
            [ProtocolType.Ums] = umsSource,
            [ProtocolType.Simulated] = simulatedSource
        };

        // 根据操作系统添加对应的 MTP 实现（哪个不为空就加哪个）
        if (mtpSource is not null)
            dict[ProtocolType.Mtp] = mtpSource;
        else if (linuxMtpSource is not null)
            dict[ProtocolType.Mtp] = linuxMtpSource;

        _sources = dict;
    }

    public ICollectSource GetFor(ProtocolType protocol)
    {
        return _sources.TryGetValue(protocol, out var source)
            ? source
            : _sources[ProtocolType.Ums]; // 降级回模拟
    }
}
