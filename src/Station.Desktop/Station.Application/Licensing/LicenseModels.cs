using System.Text.Json;
using System.Text.Json.Serialization;

namespace Station.Application.Licensing;

/// <summary>授权检查结果。</summary>
public sealed record LicenseCheckResult(
    Station.Contracts.LicenseStatus Status,
    DateTime? ExpiresAt,
    int DaysLeft,
    bool IsValid,
    string Message);
