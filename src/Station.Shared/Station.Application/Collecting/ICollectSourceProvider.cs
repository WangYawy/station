using Station.Contracts;

namespace Station.Application.Collecting;

/// <summary>
/// 按设备协议选择采集源：一台采集站可同时接入多个设备（全 UMS / 全 MTP / 混合），
/// 每个任务按其记录仪的实际协议路由到对应的连接/读取实现。
/// </summary>
public interface ICollectSourceProvider
{
    ICollectSource GetFor(ProtocolType protocol);
}

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
