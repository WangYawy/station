using Station.Application.Users.Commands;

namespace Station.Application.Users;

/// <summary>
/// 用户应用服务（写侧）。
/// 读侧请使用 <c>IUserQueryService</c>。
/// 账号相关写操作见 <c>IAccountService</c>。
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 创建用户；当 UserName + Password 同时提供时，同事务创建登录账号。
    /// 这是"用户 + 首账号"复合用例，故保留在用户服务里。
    /// </summary>
    Task<OpResult> CreateAsync(CreateUserCommand command, CancellationToken ct = default);

    /// <summary>更新用户基本信息、所属部门、启用状态。</summary>
    Task<OpResult> UpdateAsync(UpdateUserCommand command, CancellationToken ct = default);

    /// <summary>
    /// 停用用户。同事务内：解绑并禁用其下所有账号、清除用户角色关联。
    /// 软停用（IsActive=false），不物理删除。
    /// </summary>
    Task<OpResult> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>为用户分配角色（全量替换）。</summary>
    Task<OpResult> AssignRolesAsync(
        AssignUserRolesCommand command, CancellationToken ct = default);
}
