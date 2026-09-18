using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.Audit;

/// <summary>审计日志服务：写关键操作审计、按条件检索（后端分页）、枚举操作类型。</summary>
public interface IAuditLogService
{
    /// <summary>写入一条审计日志。</summary>
    Task WriteAsync(AuditLog entry);

    /// <summary>
    /// 按条件检索审计日志（后端分页）。<br/>
    /// 所有条件均为可选，null 表示不过滤。
    /// </summary>
    /// <param name="keyword">关键词：模糊匹配 操作人姓名/账号、操作对象、详情；空白视为不过滤。</param>
    /// <param name="operationType">操作类型，精确匹配；null 或空白表示全部。</param>
    /// <param name="from">起始时间（含），按天取整到 00:00:00。</param>
    /// <param name="to">结束时间（含当日），内部会 +1 天做左闭右开。</param>
    /// <param name="success">结果：true=成功，false=失败，null=全部。</param>
    /// <param name="pageIndex">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PageResult<AuditLogDto>> SearchAsync(
        string? keyword = null,
        string? operationType = null,
        DateTime? from = null,
        DateTime? to = null,
        bool? success = null,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>枚举下拉框可选的操作类型。</summary>
    Task<IReadOnlyList<string>> GetOperationTypesAsync(CancellationToken ct = default);
}
