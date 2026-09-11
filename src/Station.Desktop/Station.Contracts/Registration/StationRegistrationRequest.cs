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

    /// <summary>采集站内置 Web 地址（平台预览代理用，可空）。</summary>
    public string? StationBaseUrl { get; init; }

    /// <summary>采集站归属部门（平台组织，可空；平台侧可后续调整）。</summary>
    public long? DeptId { get; init; }
}
