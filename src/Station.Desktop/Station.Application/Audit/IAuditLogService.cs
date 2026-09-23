using Station.Domain.Audit;
using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.Audit;

/// <summary>审计日志服务：写关键操作审计、按条件检索（后端分页）、枚举操作类型。</summary>
public interface IAuditLogService
{
    /// <summary>写入一条审计日志。</summary>
    Task WriteAsync(AuditLog entry, CancellationToken ct = default);
    /// <summary>
    /// 按条件检索审计日志（后端分页）。所有条件均可选，null 表示不过滤。
    /// </summary>
    /// <param name="from">起始日期（含），按天取整到 00:00:00。</param>
    /// <param name="to">结束日期（含当日），内部 +1 天做左闭右开。</param>
    /// <param name="operationType">操作类型 Code，精确匹配。</param>
    /// <param name="operatorNo">操作人 UserNo，精确匹配。</param>
    /// <param name="success">结果：true=成功，false=失败，null=全部。</param>
    /// <param name="detailKeyword">仅对 Detail 列做中缀模糊匹配。</param>
    Task<PageResult<AuditLogDto>> SearchAsync(
        DateTime? from = null,
        DateTime? to = null,
        string? operationType = null,
        string? operatorNo = null,
        bool? success = null,
        string? detailKeyword = null,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>操作类型受控词表，同步零 IO。</summary>
    IReadOnlyList<AuditOperationTypeDescriptor> GetOperationTypes();

    /// <summary>
    /// 操作人下拉：按当前登录人角色过滤。<br/>
    /// admin → 全部；其他 → 仅自己。未登录返回空。
    /// </summary>
    Task<IReadOnlyList<OperatorOptionDto>> GetSelectableOperatorsAsync(CancellationToken ct = default);

    /// <summary>
    /// 归档：把 <c>CreatedAt &lt; now - retentionDays</c> 的记录迁到 AuditLogArchive。<br/>
    /// 返回迁移条数；失败抛异常由调用方记录。
    /// </summary>
    Task<int> ArchiveAsync(int retentionDays, CancellationToken ct = default);


    /// <summary>
    /// 按操作类型取最近 N 条审计记录（按 CreatedAt 倒序）。
    /// 用于设置页"最近安全变更审计"列表。
    /// </summary>
    /// <param name="operationTypes">操作类型过滤；为空则不过滤。</param>
    /// <param name="take">返回条数上限。</param>
    Task<List<AuditLog>> GetRecentAsync(
        IEnumerable<string>? operationTypes = null,
        int take = 20,
        CancellationToken ct = default);
}
