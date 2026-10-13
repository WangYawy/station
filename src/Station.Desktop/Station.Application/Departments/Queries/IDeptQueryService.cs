using Station.Application.Departments.Dtos;
using Station.Data.Paging;

namespace Station.Application.Departments;

/// <summary>
/// 部门读模型查询服务。
/// 只投影 DTO，不重建领域聚合；不参与写事务。
/// </summary>
public interface IDeptQueryService
{
    /// <summary>分页查询部门列表。</summary>
    Task<PageResult<DeptListItemDto>> PageAsync(
        DeptPageQuery query, CancellationToken ct = default);

    /// <summary>按主键查询详情（不存在或已删则返回 null）。</summary>
    Task<DeptDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>按编码查询详情。</summary>
    Task<DeptDetailDto?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>获取全部启用部门的下拉选项。</summary>
    Task<IReadOnlyList<DeptOptionDto>> GetOptionsAsync(CancellationToken ct = default);

    /// <summary>获取组织结构树（默认仅启用节点）。</summary>
    Task<IReadOnlyList<DeptTreeNodeDto>> GetTreeAsync(
        bool onlyActive = true, CancellationToken ct = default);

    /// <summary>
    /// 批量获取 Id → Name 映射。
    /// 用于填充列表/详情中的"部门名称"字段，避免重建聚合。
    /// </summary>
    Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>判断部门是否存在（供写流程做外键校验，仅做存在性判断时优先用它，不用 Repository）。</summary>
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
}
