using Station.Application.Departments.Commands;
using Station.Application.Users;

namespace Station.Application.Departments;

/// <summary>
/// 部门应用服务（写侧）。
/// 读侧请使用 <c>IDeptQueryService</c>。
/// </summary>
public interface IDeptService
{
    /// <summary>创建部门，返回新部门 Id。</summary>
    Task<long> CreateAsync(CreateDeptCommand command, CancellationToken ct = default);

    /// <summary>更新部门名称、排序、上级、启用状态。Code 不可变。</summary>
    Task<OpResult> UpdateAsync(UpdateDeptCommand command, CancellationToken ct = default);

    /// <summary>删除部门。存在下级部门或在职用户时拒绝。</summary>
    Task<OpResult> DeleteAsync(long id, CancellationToken ct = default);
}
