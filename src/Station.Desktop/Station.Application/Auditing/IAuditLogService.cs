using Station.Application.Auditing.Dtos;
using Station.Domain.Auditing;

namespace Station.Application.Auditing;

/// <summary>
/// 审计日志应用服务（写侧）。
/// 
/// 职责：
///   - 写入审计日志（校验 Id / CreatedAt）
///   - 提供操作类型词表（零 IO 常量）
///   - 提供操作人下拉目录（查 IUserQueryService）
///   - 提供最近日志（用于首页/看板）
/// 
/// 读侧查询（Search）请使用 <c>IAuditLogQueryService</c>。
/// </summary>
public interface IAuditLogService
{
    /// <summary>写入审计日志（自动补 Id 与 CreatedAt）。</summary>
    Task WriteAsync(AuditLog entry, CancellationToken ct = default);

    /// <summary>操作类型词表（代码常量，零 IO）。</summary>
    IReadOnlyList<AuditOperationTypeDescriptor> GetOperationTypes();

    /// <summary>最近 N 条日志（按 CreatedAt 降序）。</summary>
    Task<List<AuditLog>> GetRecentAsync(
        IEnumerable<string>? operationTypes = null,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>可选操作人下拉项（管理员看全部，非管理员只看自己）。</summary>
    Task<IReadOnlyList<OperatorOptionDto>> GetSelectableOperatorsAsync(
        CancellationToken ct = default);

    /// <summary>归档（保留接口，暂不实现）。</summary>
    Task<int> ArchiveAsync(int retentionDays, CancellationToken ct = default);
}
