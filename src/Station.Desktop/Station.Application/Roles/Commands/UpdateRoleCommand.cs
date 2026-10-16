using Station.Domain.Roles;

namespace Station.Application.Roles.Commands;

/// <summary>
/// 更新角色命令。
/// 预置角色（IsSystem=true）的 Code 不可变，由领域层校验。
/// </summary>
public sealed class UpdateRoleCommand
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DataScope DataScope { get; init; } = DataScope.Self;
    public bool IsActive { get; init; }
}
