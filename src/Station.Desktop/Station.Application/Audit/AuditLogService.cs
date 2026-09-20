using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Station.Application.IdGenerators;
using Station.Application.Session;
using Station.Application.Users;
using Station.Contracts.Api;
using Station.Domain;
using Station.Domain.Audit;
using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.Audit;

public sealed class AuditLogService : IAuditLogService
{
    private const int DefaultPageSize = 20;

    private readonly IRepository<AuditLog> _auditLogs;
    private readonly IIdGenerator _idGenerator;
    private readonly IUserService _users;
    private readonly ISessionManager _sessions;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IRepository<AuditLog> auditLogs,
        IIdGenerator idGenerator,
        IUserService users,
        ISessionManager sessions,
        ILogger<AuditLogService> logger)
    {
        _auditLogs = auditLogs;
        _idGenerator = idGenerator;
        _users = users;
        _sessions = sessions;
        _logger = logger;
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
        DateTime? from = null,
        DateTime? to = null,
        string? operationType = null,
        string? operatorNo = null,
        bool? success = null,
        string? detailKeyword = null,
        int pageIndex = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        // ---------- 1. 归一化输入 ----------
        DateTime? fromValue = from?.Date;
        DateTime? toValue = to?.Date.AddDays(1);   // 左闭右开
        string? type = string.IsNullOrWhiteSpace(operationType) ? null : operationType.Trim();
        string? opNo = string.IsNullOrWhiteSpace(operatorNo) ? null : operatorNo.Trim();
        string? detail = string.IsNullOrWhiteSpace(detailKeyword) ? null : detailKeyword.Trim();
        int? resultCode = success is null ? null : success.Value ? 1 : 0;

        // ---------- 2. 拼过滤条件（从 null 起步，逐个 And） ----------
        Expression<Func<AuditLog, bool>>? predicate = null;

        if (fromValue is { } fv)
            predicate = predicate.And(x => x.CreatedAt >= fv);

        if (toValue is { } tv)
            predicate = predicate.And(x => x.CreatedAt < tv);

        if (type is not null)
            predicate = predicate.And(x => x.OperationType == type);

        if (opNo is not null)
            predicate = predicate.And(x => x.OperatorAccount == opNo);

        if (resultCode is { } rc)
            predicate = predicate.And(x => x.Result == rc);

        if (detail is not null)
            predicate = predicate.And(x => x.Detail != null && x.Detail.Contains(detail));

        // ---------- 3. 构造 PageQuery ----------
        var query = new PageQuery<AuditLog>
        {
            Predicate = predicate,                 // 可能为 null（无任何条件）
            PageIndex = pageIndex < 1 ? 1 : pageIndex,
            PageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, AppConst.MaxPageSize),
            CountTotal = true,
        };

        // ---------- 4. 执行 ----------
        var page = await _auditLogs.ToPageAsync(
            query,
            x => x.CreatedAt,
            descending: true,
            ct);

        // ---------- 5. 映射 DTO ----------
        var items = new List<AuditLogDto>(page.Items.Count);
        foreach (var log in page.Items)
        {
            items.Add(new AuditLogDto
            {
                TimeText = log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Operator = string.IsNullOrWhiteSpace(log.OperatorName)
                                ? (string.IsNullOrWhiteSpace(log.OperatorAccount) ? "system" : log.OperatorAccount)
                                : log.OperatorName,
                Type = log.OperationType,
                TypeText = AuditOperationTypes.DisplayOf(log.OperationType),
                Target = log.Target ?? string.Empty,
                Detail = log.Detail ?? string.Empty,
                ResultText = log.Result == 1 ? "成功" : "失败",
                ResultColor = log.Result == 1 ? "#22c55e" : "#ef4444",
            });
        }

        return new PageResult<AuditLogDto>
        {
            Items = items,
            Total = page.Total,
            PageIndex = page.PageIndex,
            PageSize = page.PageSize,
        };
    }

    /// <summary>操作类型词表来自代码常量，零 IO。</summary>
    public IReadOnlyList<AuditOperationTypeDescriptor> GetOperationTypes()
       => AuditOperationTypes.All;

    // ---------- 操作人目录 ----------

    public async Task<IReadOnlyList<OperatorOptionDto>> GetSelectableOperatorsAsync(CancellationToken ct = default)
    {
        var current = _sessions.Current;
        if (current is null) return [];

        var users = await _users.GetUsersAsync();

        var isAdmin = current.Roles?.Any(r =>
            string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)) == true;

        // 非管理员只看自己；current.UserName 为登录账号，对应 UserDto.UserNo
        var filtered = isAdmin
            ? users
            : users.Where(u => string.Equals(u.UserNo, current.UserName, StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Select(u => new OperatorOptionDto(u.UserNo, u.Name))
            .ToList();
    }

    // ---------- 归档 ----------

    public async Task<int> ArchiveAsync(int retentionDays, CancellationToken ct = default)
    {
        return 0;

        //        if (retentionDays <= 0) return 0;

        //        var cutoff = DateTime.Now.AddDays(-retentionDays);

        //        // 用裸 SQL INSERT...SELECT，百万级不落内存
        //        const string insertSql = @"
        //INSERT INTO AuditLogArchive
        //    (Id, CreatedAt, OperatorAccount, OperatorName, OperationType, Target, Detail, Result)
        //SELECT Id, CreatedAt, OperatorAccount, OperatorName, OperationType, Target, Detail, Result
        //FROM AuditLog
        //WHERE CreatedAt < @cutoff";

        //        const string deleteSql = @"DELETE FROM AuditLog WHERE CreatedAt < @cutoff";

        //        await _db.Ado.BeginTranAsync();
        //        try
        //        {
        //            var inserted = await _db.Ado.ExecuteCommandAsync(insertSql, new { cutoff });
        //            if (inserted > 0)
        //            {
        //                await _db.Ado.ExecuteCommandAsync(deleteSql, new { cutoff });
        //            }
        //            await _db.Ado.CommitTranAsync();
        //            return inserted;
        //        }
        //        catch
        //        {
        //            await _db.Ado.RollbackTranAsync();
        //            throw;
        //        }
    }
}
