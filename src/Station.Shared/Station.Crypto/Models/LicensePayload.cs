namespace Station.Crypto;

/// <summary>
/// 授权 payload（canonical 结构）。
/// 序列化后的 JSON 是签名对象，字段名变更会破坏已签发授权。
/// </summary>
public sealed record LicensePayload(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt);
