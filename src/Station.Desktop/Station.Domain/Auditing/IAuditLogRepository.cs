namespace Station.Domain.Auditing;

/// <summary>
/// 审计日志写仓储。
/// 只提供写入 + 少量读侧辅助，不做聚合语义，因为 AuditLog 不是聚合根。
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>写入一条审计日志。</summary>
    Task AddAsync(AuditLog entry, CancellationToken ct = default);

    /// <summary>按操作类型查最近 N 条（供首页/看板用）。</summary>
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(
        IReadOnlyCollection<string>? operationTypes,
        int take,
        CancellationToken ct = default);
}
