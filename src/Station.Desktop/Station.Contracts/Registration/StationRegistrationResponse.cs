namespace Station.Contracts.Registration;

/// <summary>注册结果：平台分配 StationId，采集站本地缓存。</summary>
public sealed record StationRegistrationResponse
{
    public long StationId { get; init; }

    public required string StationCode { get; init; }

    public LicenseStatus LicenseStatus { get; init; }

    /// <summary>是否本次完成首次注册。</summary>
    public bool IsRegistered { get; init; }

    public long ConfigVersion { get; init; }
}
