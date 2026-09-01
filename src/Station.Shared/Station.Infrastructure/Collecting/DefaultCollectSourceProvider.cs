using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Station.Application.Collecting;
using Station.Contracts;

namespace Station.Infrastructure.Collecting;

/// <summary>共享默认实现：模拟源（开发）或 UMS；桌面端用 MTP 感知实现覆盖注册。</summary>
public sealed class DefaultCollectSourceProvider : ICollectSourceProvider
{
    private readonly CollectOptions _options;
    private readonly UmsCollectSource _ums;
    private readonly SimulatedCollectSource _simulated;

    public DefaultCollectSourceProvider(
        CollectOptions options,
        UmsCollectSource ums,
        SimulatedCollectSource simulated)
    {
        _options = options;
        _ums = ums;
        _simulated = simulated;
    }

    public ICollectSource GetFor(ProtocolType protocol) =>
        _options.SourceMode == "simulated" ? _simulated : _ums;
}
