using Station.Contracts.Registration;

namespace Station.Application.Licensing;

/// <summary>
/// 机器指纹采集接口。
/// 
/// 【职责】
///   - 采集 CPU / 主板 / 磁盘 / MAC 原始字段；
///   - 组合后计算 SM3 指纹。
/// 
/// 【算法】固定 SM3，不走策略。
///   原因：指纹是机器身份标识，一旦绑定授权，算法变更会导致所有授权失效。
/// 
/// 【实现】
///   - Windows：WMI 查询
///   - Linux/信创：sysfs（DMI + 块设备）
/// </summary>
public interface IMachineFingerprintProvider
{
    /// <summary>采集原始硬件字段（未哈希）。</summary>
    MachineFingerprint CollectParts();

    /// <summary>采集并计算 SM3 指纹（Base64）。</summary>
    string CollectFingerprint();
}
