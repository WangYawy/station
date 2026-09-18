using SqlSugar;
using Station.Application.IdGenerators;
using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.Audit;

public sealed class AuditLogService : IAuditLogService
{
    /// <summary>操作类型下拉的数据采样窗口：只从未来最近的 N 条里聚合，避免全表 DISTINCT。</summary>
    private const int OperationTypeSampleSize = 2000;

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

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

    public async Task<PageResult<AuditLogDto>> SearchAsync(
        string? keyword = null,
        string? operationType = null,
        DateTime? from = null,
        DateTime? to = null,
        bool? success = null,
        int pageIndex = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        pageIndex = pageIndex < 1 ? 1 : pageIndex;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var key = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var type = string.IsNullOrWhiteSpace(operationType) ? null : operationType.Trim();
        var safeKey = key ?? string.Empty;
        var safeType = type ?? string.Empty;

        // 时间左闭右开：from 取当天 00:00:00，to 取次日 00:00:00
        var fromValue = from?.Date;
        var toValue = to?.Date.AddDays(1);
        var resultCode = success is null ? (int?)null : success.Value ? 1 : 0;

        var condition = Expressionable.Create<AuditLog>()
            .AndIF(key is not null, x =>
                (x.OperatorName != null && x.OperatorName.Contains(safeKey)) ||
                (x.OperatorAccount != null && x.OperatorAccount.Contains(safeKey)) ||
                (x.Target != null && x.Target.Contains(safeKey)) ||
                (x.Detail != null && x.Detail.Contains(safeKey)))
            .AndIF(type is not null, x => x.OperationType == safeType)
            .AndIF(fromValue.HasValue, x => x.CreatedAt >= fromValue!.Value)
            .AndIF(toValue.HasValue, x => x.CreatedAt < toValue!.Value)
            .AndIF(resultCode.HasValue, x => x.Result == resultCode!.Value);

        var page = await _auditLogs.ToPageAsync(
            pageIndex,
            pageSize,
            condition.ToExpression(),
            x => x.CreatedAt,
            OrderByType.Desc);

        var items = new List<AuditLogDto>(page.Items.Count);
        foreach (var log in page.Items)
        {
            items.Add(new AuditLogDto
            {
                TimeText = log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Operator = string.IsNullOrWhiteSpace(log.OperatorName)
                    ? string.IsNullOrWhiteSpace(log.OperatorAccount) ? "system" : log.OperatorAccount
                    : log.OperatorName,
                Type = log.OperationType,
                Target = log.Target ?? string.Empty,
                Detail = log.Detail ?? string.Empty,
                ResultText = log.Result == 1 ? "成功" : "失败",
                ResultColor = log.Result == 1 ? "#22c55e" : "#ef4444"
            });
        }

        return new PageResult<AuditLogDto>
        {
            Items = items,
            Total = page.Total,
            PageIndex = page.PageIndex,
            PageSize = page.PageSize
        };
    }

    public async Task<IReadOnlyList<string>> GetOperationTypesAsync(CancellationToken ct = default)
    {
        var recent = await _auditLogs.ToPageAsync(
            1,
            OperationTypeSampleSize,
            null,
            x => x.CreatedAt,
            OrderByType.Desc);

        return recent.Items
            .Select(x => x.OperationType)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
