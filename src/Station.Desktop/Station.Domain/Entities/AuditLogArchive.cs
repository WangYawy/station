using System;
using System.Collections.Generic;
using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 审计日志归档表：字段与 <see cref="AuditLog"/> 保持一致。<br/>
/// 只写不读，不建复合索引；仅建 CreatedAt 索引，为将来"历史查询"留后路。
/// </summary>
[SugarTable("station_audit_log_archive")]
public sealed class AuditLogArchive
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
