using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;

namespace Station.Application.Audit;

public sealed class AuditLogService : IAuditLogService
{
    private readonly IRepository<AuditLog> _auditLogs;
    private readonly IIdGenerator _idGenerator;

    public AuditLogService(IRepository<AuditLog> auditLogs, IIdGenerator idGenerator)
    {
        _auditLogs = auditLogs;
        _idGenerator = idGenerator;
    }

    public async Task WriteAsync(AuditLog entry)
    {
        if (entry.CreatedAt == default)
        {
            entry.CreatedAt = DateTime.Now;
        }

        if (entry.Id == 0)
        {
            entry.Id = _idGenerator.NextId();
        }

        await _auditLogs.InsertAsync(entry);
    }

    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count)
    {
        var page = await _auditLogs.ToPageAsync(1, count, orderBy: x => x.CreatedAt, orderType: SqlSugar.OrderByType.Desc);
        return page.Items;
    }
}
