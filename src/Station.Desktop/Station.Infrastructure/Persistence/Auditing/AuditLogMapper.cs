using Station.Domain.Auditing;

namespace Station.Infrastructure.Persistence.Auditing;

internal static class AuditLogMapper
{
    public static AuditLog ToDomain(AuditLogPo po)
        => new()
        {
            Id = po.Id,
            OperatorAccount = po.OperatorAccount,
            OperatorName = po.OperatorName,
            OperatorUserId = po.OperatorUserId,
            DeptId = po.DeptId,
            SourceIp = po.SourceIp,
            SourceClient = po.SourceClient,
            OperationType = po.OperationType,
            Target = po.Target,
            Detail = po.Detail,
            Result = po.Result,
            CreatedAt = po.CreatedAt
        };

    public static AuditLogPo ToPo(AuditLog entry)
        => new()
        {
            Id = entry.Id,
            OperatorAccount = entry.OperatorAccount,
            OperatorName = entry.OperatorName,
            OperatorUserId = entry.OperatorUserId,
            DeptId = entry.DeptId,
            SourceIp = entry.SourceIp,
            SourceClient = entry.SourceClient,
            OperationType = entry.OperationType,
            Target = entry.Target,
            Detail = entry.Detail,
            Result = entry.Result,
            CreatedAt = entry.CreatedAt
        };
}
