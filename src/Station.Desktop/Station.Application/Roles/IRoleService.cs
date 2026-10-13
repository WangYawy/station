using Station.Application.Roles.Commands;
using Station.Application.Users;

namespace Station.Application.Roles;

/// <summary>
/// 角色应用服务（写侧）。
/// 读侧请使用 <c>IRoleQueryService</c>。
/// </summary>
public interface IRoleService
{
    /// <summary>创建自定义角色。IsSystem 恒为 false，由系统内部决定。</summary>
    Task<long> CreateAsync(CreateRoleCommand command, CancellationToken ct = default);

    /// <summary>更新角色。预置角色编码不可修改（由聚合根校验）。</summary>
    Task<OpResult> UpdateAsync(UpdateRoleCommand command, CancellationToken ct = default);

    /// <summary>删除角色。预置角色不可删除；同时清理角色-权限、用户-角色关联。</summary>
    Task<OpResult> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>设置角色权限（全量替换）。</summary>
    Task<OpResult> SetPermissionsAsync(
        SetRolePermissionsCommand command, CancellationToken ct = default);
}
