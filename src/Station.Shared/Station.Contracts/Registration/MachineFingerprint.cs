namespace Station.Contracts.Registration;

/// <summary>
/// 机器指纹原始特征（CPU+主板+磁盘+MAC 组合）。
/// 具体指纹串由各端基础设施层对 ToRaw() 做 SM3 计算，契约只定义数据形态。
/// </summary>
public sealed record MachineFingerprint
{
    public required string CpuSerial { get; init; }

    public required string MotherboardSerial { get; init; }

    public required string DiskSerial { get; init; }

    public required string MacAddress { get; init; }

    /// <summary>稳定的原始拼接表示（字段顺序固定，供哈希计算）。</summary>
    public string ToRaw() => $"{CpuSerial}|{MotherboardSerial}|{DiskSerial}|{MacAddress}";
}
