namespace Station.Domain.Cryptography;

/// <summary>加密算法策略（POCO）。</summary>
public sealed class SysCryptoPolicy
{
    public long Id { get; set; }
    public string UsageCode { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string? SecondaryAlgorithm { get; set; }
    public string? ParamsJson { get; set; }
    public string? LegacyAlgorithmsJson { get; set; }
    public bool AllowLegacy { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string Source { get; set; } = "Local";
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
