namespace Station.Contracts.Registration;

/// <summary>采集站注册请求（首次连接平台，按采集站编号 + 机器指纹）。</summary>
public sealed record StationRegistrationRequest
{
    public required string StationCode { get; init; }

    public required MachineFingerprint MachineFingerprint { get; init; }

    public required string OsVersion { get; init; }

    /// <summary>x86_64 / arm64。</summary>
    public required string CpuArch { get; init; }

    public required string SoftwareVersion { get; init; }

    public int UsbPortCount { get; init; }
}
