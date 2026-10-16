using SqlSugar;

namespace Station.Infrastructure.Persistence.Auditing;

[SugarTable("station_audit_log_archive")]
public sealed class AuditLogArchivePo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? OperatorAccount { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? OperatorName { get; set; }
    [SugarColumn(IsNullable = true)] public long? OperatorUserId { get; set; }
    [SugarColumn(IsNullable = true)] public long? DeptId { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? SourceIp { get; set; }
    [SugarColumn(Length = 32)] public string OperationType { get; set; } = "";
    [SugarColumn(IsNullable = true, Length = 128)] public string? Target { get; set; }
    [SugarColumn(IsNullable = true, Length = 512)] public string? Detail { get; set; }
    public int Result { get; set; }
    public DateTime CreatedAt { get; set; }
}
