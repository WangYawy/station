using Station.Domain.Cryptography;

namespace Station.Infrastructure.Persistence.Cryptography;

internal static class SysCryptoPolicyMapper
{
    public static SysCryptoPolicy ToDomain(SysCryptoPolicyPo po) => new()
    {
        Id = po.Id,
        UsageCode = po.UsageCode,
        Algorithm = po.Algorithm,
        SecondaryAlgorithm = po.SecondaryAlgorithm,
        ParamsJson = po.ParamsJson,
        LegacyAlgorithmsJson = po.LegacyAlgorithmsJson,
        AllowLegacy = po.AllowLegacy,
        Enabled = po.Enabled,
        Source = po.Source,
        UpdatedAt = po.UpdatedAt,
        UpdatedBy = po.UpdatedBy
    };

    public static SysCryptoPolicyPo ToPo(SysCryptoPolicy p) => new()
    {
        Id = p.Id,
        UsageCode = p.UsageCode,
        Algorithm = p.Algorithm,
        SecondaryAlgorithm = p.SecondaryAlgorithm,
        ParamsJson = p.ParamsJson,
        LegacyAlgorithmsJson = p.LegacyAlgorithmsJson,
        AllowLegacy = p.AllowLegacy,
        Enabled = p.Enabled,
        Source = p.Source,
        UpdatedAt = p.UpdatedAt,
        UpdatedBy = p.UpdatedBy
    };
}
