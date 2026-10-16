namespace Station.Device.Port;

/// <summary>
/// 物理 USB 端口解析。跨平台实现分别处理 SetupAPI / sysfs。
/// 与设备发现无关，只关心"设备挂载点在物理上位于哪个端口"。
/// </summary>
public interface IUsbPortResolver
{
    /// <summary>
    /// 解析物理端口标识。
    /// Windows 输出：<c>Win:Hub{XXXX}:Port{XXXX}</c>
    /// Linux   输出：<c>Lin:{bus}-{hub}.{port}</c>
    /// 失败返回 null。
    /// </summary>
    string? ResolvePhysicalPortId(string deviceRoot);

    /// <summary>当前平台是否可用（权限 / 驱动 / 系统支持均满足）。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用原因；可用时为 null。</summary>
    string? UnavailableReason { get; }
}
