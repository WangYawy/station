using SqlSugar;

namespace Station.Platform.Domain.Entities;

/// <summary>文件归属修正留痕（平台侧关键修正附 SM2 签名）。</summary>
[SugarTable("platform_file_correction")]
[SugarIndex("idx_correction_file", nameof(FileNo), OrderByType.Asc)]
public sealed class PlatformFileCorrection
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string FileNo { get; set; } = string.Empty;

    public long StationId { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? OldUserNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? NewUserNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? OldDeptCode { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? NewDeptCode { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? OperatorAccount { get; set; }

    public DateTime CorrectedAt { get; set; }

    /// <summary>SM2 签名（平台私钥），未配置密钥时为 "unsigned"。</summary>
    [SugarColumn(Length = 256)]
    public string Signature { get; set; } = string.Empty;
}
