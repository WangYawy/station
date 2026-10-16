using Station.Domain.Licensing;

namespace Station.Infrastructure.Persistence.Licensing;

internal static class LicenseMapper
{
    public static License ToDomain(LicensePo po)
        => License.Restore(
            po.Id, po.LicenseKey, po.ProductCode, po.StationCode,
            po.Fingerprint, po.PayloadEnc, po.IssuedAt, po.ExpiresAt,
            po.Status, po.ActivatedAt, po.IsActive);

    public static LicensePo ToPo(License l) => new()
    {
        Id = l.Id,
        LicenseKey = l.LicenseKey,
        ProductCode = l.ProductCode,
        StationCode = l.StationCode,
        Fingerprint = l.Fingerprint,
        PayloadEnc = l.PayloadEnc,
        IssuedAt = l.IssuedAt,
        ExpiresAt = l.ExpiresAt,
        Status = l.Status,
        ActivatedAt = l.ActivatedAt,
        IsActive = l.IsActive
    };
}
