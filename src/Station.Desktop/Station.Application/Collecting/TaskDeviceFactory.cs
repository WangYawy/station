using Station.Contracts;
using Station.Device.Detection;
using Station.Domain.Collecting;

namespace Station.Application.Collecting;

/// <summary>
/// 从 CollectTask 统一构造 DetectedDevice。
///
/// 【用途】
///   - 采集执行 / 擦除 / 重试等下游逻辑统一从这里取 DetectedDevice
///   - 消除三处手拼（StartAsync / RunCollectAsync / TryErase）
///   - DeviceRef → DetectedDevice 是 Application 层职责，Domain 不依赖 Station.Device
/// </summary>
public static class TaskDeviceFactory
{
    /// <summary>从聚合根构造 DetectedDevice，与探测时完全等价。</summary>
    public static DetectedDevice FromCollectTask(CollectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(task.Device);

        var d = task.Device;
        return new DetectedDevice(
            Key: d.Key,
            Name: d.Name,
            Serial: d.Serial,
            Protocol: d.Protocol,
            Root: d.Root,
            PhysicalPortId: d.PhysicalPortId,
            LinkState: DeviceLinkState.Bound);
    }

    /// <summary>从 DeviceRef 构造 DetectedDevice（供快照等场景使用）。</summary>
    public static DetectedDevice FromDeviceRef(DeviceRef deviceRef)
    {
        ArgumentNullException.ThrowIfNull(deviceRef);
        return new DetectedDevice(
            Key: deviceRef.Key,
            Name: deviceRef.Name,
            Serial: deviceRef.Serial,
            Protocol: deviceRef.Protocol,
            Root: deviceRef.Root,
            PhysicalPortId: deviceRef.PhysicalPortId,
            LinkState: DeviceLinkState.Bound);
    }
}
