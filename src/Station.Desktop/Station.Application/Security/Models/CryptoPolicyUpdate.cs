namespace Station.Application.Security.Models;

/// <summary>更新加密策略的入参。</summary>
public sealed record CryptoPolicyUpdate(
    string Algorithm,
    string? SecondaryAlgorithm = null,
    string? ParamsJson = null,
    bool? AllowLegacy = null,
    bool? Enabled = null,
    string? Remark = null);
