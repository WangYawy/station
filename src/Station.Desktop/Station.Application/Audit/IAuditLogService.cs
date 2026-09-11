using Station.Domain.Entities;

namespace Station.Application.Audit;

/// <summary>审计日志服务：写关键操作审计、查询最近记录。</summary>
public interface IAuditLogService
{
    Task WriteAsync(AuditLog entry);

    Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count);
}
