using Station.Contracts.Registration;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Licensing;

/// <summary>
/// 机器指纹采集：CPU + 主板 + 磁盘 + MAC 原始字段，指纹 = SM3(ToRaw())。
/// Windows 走 WMI；Linux/信创 走 sysfs（DMI/块设备）。
/// </summary>
public interface IMachineFingerprintProvider
{
    MachineFingerprint CollectParts();

    string CollectFingerprint() => Sm3Checksum.ComputeString(CollectParts().ToRaw());
}
