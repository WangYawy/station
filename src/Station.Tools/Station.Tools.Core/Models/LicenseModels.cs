namespace Station.Tools.Core.Models;

/// <summary>授权文件内容（canonical 结构）。</summary>
public sealed record LicensePayload(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt);

/// <summary>完整授权文件。</summary>
public sealed record LicenseFile(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string PayloadCipher,
    string Algo,
    string Signature);

/// <summary>授权检查结果。</summary>
public sealed record LicenseCheckResult(
    bool Valid,
    string Message,
    LicensePayload? Payload);
