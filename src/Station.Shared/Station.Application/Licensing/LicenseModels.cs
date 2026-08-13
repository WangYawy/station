using System.Text.Json;
using Station.Infrastructure.Security;

namespace Station.Application.Licensing;

/// <summary>授权文件（station.lic）：内部工具生成，采集站校验。</summary>
public sealed record LicenseFile(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string Signature);

public static class LicenseFileCodec
{
    public static string Serialize(LicenseFile file) => JsonSerializer.Serialize(file);

    public static LicenseFile Parse(string json) =>
        JsonSerializer.Deserialize<LicenseFile>(json) ?? throw new InvalidOperationException("授权文件格式无效");

    public static string Canonical(LicenseFile file) =>
        $"{file.LicenseKey}|{file.ProductCode}|{file.StationCode}|{file.Fingerprint}|{file.IssuedAt:O}|{file.ExpiresAt:O}";

    public static string Sign(LicenseFile file, string signingKey) =>
        Sm3Checksum.ComputeHmac(signingKey, Canonical(file));

    public static bool Verify(LicenseFile file, string signingKey) =>
        string.Equals(
            file.Signature,
            Sign(file with { Signature = string.Empty }, signingKey),
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>授权检查结果。</summary>
public sealed record LicenseCheckResult(
    Station.Contracts.LicenseStatus Status,
    DateTime? ExpiresAt,
    int DaysLeft,
    bool IsValid,
    string Message);
