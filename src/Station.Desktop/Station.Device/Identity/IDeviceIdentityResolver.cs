using Station.Device.Detection;

namespace Station.Device.Identity;

/// <summary>
/// 把探测结果归一为稳定 DeviceKey。
/// 优先级：PhysicalPortId > Serial > Root。
/// 无状态，纯计算。
/// </summary>
public interface IDeviceIdentityResolver
{
    /// <summary>
    /// 生成稳定 Key。
    /// 输出格式：<c>PORT:{id}</c> / <c>SN:{serial}</c> / <c>ROOT:{root}</c>，统一大写。
    /// </summary>
    string Resolve(DetectedDevice device);
}
