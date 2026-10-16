using Station.Application.Auditing.Dtos;
using Station.Data.Paging;

namespace Station.Application.Auditing;

/// <summary>
/// 审计日志读侧查询服务。实现位于 Infrastructure，直接投影 DTO。
/// </summary>
public interface IAuditLogQueryService
{
    Task<PageResult<AuditLogDto>> SearchAsync(
        DateTime? from,
        DateTime? to,
        string? operationType,
        string? operatorNo,
        bool? success,
        string? detailKeyword,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);
}
