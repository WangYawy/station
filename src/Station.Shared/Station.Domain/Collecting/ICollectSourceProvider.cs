using Station.Contracts;

namespace Station.Domain.Collecting;

/// <summary>
/// 按设备协议选择采集源：一台采集站可同时接入多个设备（全 UMS / 全 MTP / 混合），
/// 每个任务按其记录仪的实际协议路由到对应的连接/读取实现。
/// </summary>
public interface ICollectSourceProvider
{
    ICollectSource GetFor(ProtocolType protocol);
}
