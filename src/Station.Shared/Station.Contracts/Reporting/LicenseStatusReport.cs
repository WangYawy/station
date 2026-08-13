namespace Station.Contracts.Reporting;

/// <summary>授权状态心跳上报（随平台同步轮次上报）。</summary>
public sealed record LicenseStatusReport
{
    public long StationId { get; init; }

    public LicenseStatus LicenseStatus { get; init; }

    public DateTime? ExpiresAt { get; init; }

    public int DaysLeft { get; init; }

    public string? Signature { get; init; }
}

public static class LicenseStatusReportSignature
{
    public static string Canonical(LicenseStatusReport report) =>
        $"{report.StationId}|{(int)report.LicenseStatus}|{report.ExpiresAt?.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}|{report.DaysLeft}";
}
