namespace Station.Crypto;

/// <summary>
/// 完整授权文件（只签名不加密）。
///
/// 字段说明：
///   - Payload：canonical JSON 明文（包含 LicenseKey/ProductCode/StationCode/Fingerprint/IssuedAt/ExpiresAt）
///   - SignatureAlgorithm：签名算法标识（CryptoAlgorithm.Sm2Sm3 / RsaSha256）
///   - Signature：对 Payload 字节的签名（Base64）
///
/// ⚠️ 持久化契约：字段名变更会破坏已签发授权文件的解析。
/// </summary>
public sealed record LicenseFile(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string Payload,
    string SignatureAlgorithm,
    string Signature);
