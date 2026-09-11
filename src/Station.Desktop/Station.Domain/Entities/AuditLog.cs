using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 审计日志（操作日志）：登录、查询、导入导出、删除、擦除、配置修改、绑定写入等关键操作。
/// 存储原文，展示/导出时脱敏。Result: 1=成功 0=失败。
/// </summary>
[SugarTable("station_audit_log")]
[SugarIndex("idx_audit_created", nameof(CreatedAt), OrderByType.Desc)]
public sealed class AuditLog
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? OperatorAccount { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? OperatorName { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? OperatorUserId { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? SourceIp { get; set; }

    [SugarColumn(Length = 32)]
    public string OperationType { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Target { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Detail { get; set; }

    public int Result { get; set; }

    public DateTime CreatedAt { get; set; }
}
