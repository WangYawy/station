using Station.Contracts;

namespace Station.Domain.Licensing;

/// <summary>
/// 授权聚合根。
/// 状态机：Activated → Expired / Revoked / Deactivated
/// </summary>
public sealed class License
{
    public long Id { get; private set; }
    public string LicenseKey { get; private set; } = string.Empty;
    public string ProductCode { get; private set; } = string.Empty;
    public string StationCode { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public string? PayloadEnc { get; private set; }
    public DateTime IssuedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public LicenseStatus Status { get; private set; }
    public DateTime? ActivatedAt { get; private set; }
    public bool IsActive { get; private set; }

    private License() { }

    public static License Create(
        long id, string licenseKey, string productCode, string stationCode,
        string fingerprint, string? payloadEnc,
        DateTime issuedAt, DateTime expiresAt, DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new ArgumentException("授权码不能为空。", nameof(licenseKey));
        if (expiresAt <= issuedAt)
            throw new ArgumentException("到期时间必须晚于签发时间。", nameof(expiresAt));

        return new License
        {
            Id = id,
            LicenseKey = licenseKey,
            ProductCode = productCode,
            StationCode = stationCode,
            Fingerprint = fingerprint,
            PayloadEnc = payloadEnc,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Status = LicenseStatus.Activated,
            ActivatedAt = now,
            IsActive = true
        };
    }

    public static License Restore(
        long id, string licenseKey, string productCode, string stationCode,
        string fingerprint, string? payloadEnc,
        DateTime issuedAt, DateTime expiresAt,
        LicenseStatus status, DateTime? activatedAt, bool isActive)
        => new()
        {
            Id = id,
            LicenseKey = licenseKey,
            ProductCode = productCode,
            StationCode = stationCode,
            Fingerprint = fingerprint,
            PayloadEnc = payloadEnc,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Status = status,
            ActivatedAt = activatedAt,
            IsActive = isActive
        };

    // ---- 行为 ----

    public void Activate(DateTime now)
    {
        if (Status == LicenseStatus.Revoked)
            throw new InvalidOperationException("已吊销授权不可激活。");
        Status = LicenseStatus.Activated;
        ActivatedAt = now;
        IsActive = true;
    }

    public void Expire()
    {
        Status = LicenseStatus.Expired;
        IsActive = false;
    }

    public void Revoke()
    {
        Status = LicenseStatus.Revoked;
        IsActive = false;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    // ---- 读方法 ----

    public bool IsValidAt(DateTime now)
        => IsActive && Status == LicenseStatus.Activated && now < ExpiresAt;

    public bool IsExpiredAt(DateTime now) => now >= ExpiresAt;
}
