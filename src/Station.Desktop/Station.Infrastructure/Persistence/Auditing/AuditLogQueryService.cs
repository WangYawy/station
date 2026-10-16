using SqlSugar;
using Station.Application.Auditing;
using Station.Application.Auditing.Dtos;
using Station.Data.Paging;
using Station.Domain.Auditing;

namespace Station.Infrastructure.Persistence.Auditing;

public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private const int DefaultPageSize = 20;
    private readonly ISqlSugarClient _db;

    public AuditLogQueryService(ISqlSugarClient db) => _db = db;

    public async Task<PageResult<AuditLogDto>> SearchAsync(
        DateTime? from,
        DateTime? to,
        string? operationType,
        string? operatorNo,
        bool? success,
        string? detailKeyword,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        var pageIdx = pageIndex < 1 ? 1 : pageIndex;
        var pageSizeVal = pageSize < 1 ? DefaultPageSize : pageSize;

        var q = _db.Queryable<AuditLogPo>();

        if (from?.Date is { } fv)
            q = q.Where(x => x.CreatedAt >= fv);

        if (to?.Date.AddDays(1) is { } tv)
            q = q.Where(x => x.CreatedAt < tv);

        if (!string.IsNullOrWhiteSpace(operationType))
            q = q.Where(x => x.OperationType == operationType);

        if (!string.IsNullOrWhiteSpace(operatorNo))
            q = q.Where(x => x.OperatorAccount == operatorNo);

        if (success is { } s)
            q = q.Where(x => x.Result == (s ? 1 : 0));

        if (!string.IsNullOrWhiteSpace(detailKeyword))
        {
            var kw = detailKeyword.Trim();
            q = q.Where(x => x.Detail != null && x.Detail.Contains(kw));
        }

        q = q.OrderBy(x => x.CreatedAt, OrderByType.Desc);

        RefAsync<int> total = 0;
        var items = await q
            .Select(x => new AuditLogDto
            {
                TimeText = x.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Operator = x.OperatorName ?? x.OperatorAccount ?? "system",
                Type = x.OperationType,
                TypeText = AuditOperationTypes.DisplayOf(x.OperationType),
                Target = x.Target ?? string.Empty,
                Detail = x.Detail ?? string.Empty,
                ResultText = x.Result == 1 ? "成功" : "失败",
                ResultColor = x.Result == 1 ? "#22c55e" : "#ef4444"
            })
            .ToPageListAsync(pageIdx, pageSizeVal, total);

        return new PageResult<AuditLogDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIdx,
            PageSize = pageSizeVal
        };
    }
}
